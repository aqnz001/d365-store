using System.Security.Cryptography;
using System.Text;
using PartsPortal.Bff.Account;
using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Checkout;
using PartsPortal.Bff.Clients;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Pricing;

namespace PartsPortal.Bff.Payments;

/// <summary>How an order is settled (DR-019): a prepaid card charge, or net terms on the customer's
/// trade account (no card captured — gated on an approved credit decision).</summary>
public enum SettlementMethod
{
    Card,
    OnAccount,
}

/// <summary>
/// Request to pay for the reserved cart. Amount and currency are display-only — the BFF re-resolves
/// the authoritative amount server-side and pins the currency from config (both client values are
/// ignored). ReservationIds is likewise ignored: the server uses the reservation set it placed at
/// the gate (Golden Rule #5 / DR-020). <see cref="PaymentMethod"/> chooses card vs on-account
/// (DR-019); <see cref="PoNumber"/> is an optional purchase-order ref.
/// </summary>
public sealed record PayRequest(
    decimal Amount,
    string Currency,
    string PaymentToken,
    IReadOnlyList<string>? ReservationIds = null,
    SettlementMethod PaymentMethod = SettlementMethod.Card,
    string? PoNumber = null);

/// <summary>Outcome of paying: OrderPlaced (with the order reference), PaymentFailed,
/// CreditDeclined (on-account refused — credit not approved), NoReservation (no live checkout gate),
/// or EmptyCart.</summary>
public sealed record PayResult(string Status, string? OrderReference, string? Message);

/// <summary>
/// Takes the payment for a checked-out cart, then submits the order for queue-backed writeback
/// (TDD §6.1→§6.2): authorize via the payment provider (DR-003), and only on success build the
/// order (carrying the reservation ids + idempotency key) and enqueue it via the middleware. The
/// cart is cleared on success. Checkout never blocks on the ERP (Golden Rule #7).
/// </summary>
public sealed class PaymentService(
    ICartStore cartStore,
    IPaymentProvider payments,
    IMiddlewareApi middleware,
    IOrderHistoryStore history,
    ICheckoutSessionStore sessions,
    IConfiguration configuration)
{
    public async Task<PayResult> PayAsync(string customerAccount, PayRequest request, string correlationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cart = cartStore.Get(customerAccount);
        if (cart.Lines.Count == 0)
        {
            return new PayResult("EmptyCart", null, "Cart is empty.");
        }

        // Reserve-before-commit (Golden Rule #5): use the soft-reservation set the gate placed
        // server-side, never the client's echo. No live gate session → the customer must re-run it.
        var session = sessions.Get(customerAccount);
        if (session is null || session.ReservationIds.Count == 0)
        {
            return new PayResult("NoReservation", null, "Your checkout session has expired — please re-run the checkout gate.");
        }

        var reservationIds = session.ReservationIds;
        // Currency is pinned server-side (single-currency by design) so the client cannot name a
        // different currency for the server-resolved amount.
        var currency = configuration["Bff:Currency"] ?? "GBP";

        // Re-resolve contract pricing server-side — the charge amount and the order's locked prices
        // are authoritative here, never trusted from the client (the client-sent amount is ignored).
        var pricing = await middleware.ResolvePricingAsync(BuildPricing(customerAccount, cart), correlationId, ct);
        var unitPriceByItem = pricing.Lines
            .GroupBy(line => line.ItemNumber)
            .ToDictionary(group => group.Key, group => group.First().UnitPrice, StringComparer.Ordinal);
        // Charge the gross (net + FinOps-owned tax) — the customer pays tax-inclusive. The order's
        // locked unit price stays net, so writeback price-integrity still compares like-for-like.
        var amount = pricing.Lines.Sum(line => line.GrossEffectivePrice);

        // Stable idempotency key for both the charge and the order writeback (DR-020): derived from
        // the customer + the server-held reservation set, so a retry/double-click reuses the same key
        // — the provider returns the original charge and writeback de-dups the order.
        var idempotencyKey = DeriveIdempotencyKey(customerAccount, reservationIds);

        // Settlement (DR-019). On-account skips the card entirely and is allowed only when the
        // server-resolved credit decision is Approved — the client's choice is never trusted.
        if (request.PaymentMethod == SettlementMethod.OnAccount)
        {
            if (pricing.Decision != CreditDecision.Approved)
            {
                return new PayResult("CreditDeclined", null,
                    "This account is not approved for net terms. Please pay by card.");
            }

            // Authoritative headroom re-check: a net-terms order must fit the remaining credit (DR-023).
            if (pricing.AvailableCredit is { } headroom && headroom < amount)
            {
                return new PayResult("CreditDeclined", null,
                    "This order exceeds your remaining credit. Please pay by card or contact your account manager.");
            }
        }
        else
        {
            var payment = await payments.AuthorizeAsync(
                new PaymentRequest(amount, currency, request.PaymentToken, customerAccount, idempotencyKey), ct);
            if (payment.Status != PaymentStatus.Succeeded)
            {
                return new PayResult("PaymentFailed", null, payment.Message ?? payment.Status.ToString());
            }
        }

        var ack = await middleware.SubmitOrderAsync(
            BuildOrder(customerAccount, cart, request, unitPriceByItem, correlationId, idempotencyKey, reservationIds, currency), correlationId, ct);
        cartStore.Clear(customerAccount);
        sessions.Clear(customerAccount);

        var reference = ack.SalesOrderNumber ?? ack.OrderId ?? "pending";
        history.Record(customerAccount, new PlacedOrder(reference, DateTimeOffset.UtcNow));
        return new PayResult("OrderPlaced", reference, null);
    }

    /// <summary>
    /// Deterministic idempotency key for an order/charge: a hash of the customer + the sorted
    /// reservation ids, so honest retries collapse to one order and one charge. Callers pass the
    /// server-held reservation set (always non-empty for a real order); the empty fallback is
    /// defence-in-depth only and is unreachable from <see cref="PayAsync"/>.
    /// </summary>
    public static string DeriveIdempotencyKey(string customerAccount, IReadOnlyList<string> reservationIds)
    {
        var basis = reservationIds.Count > 0
            ? $"{customerAccount}|{string.Join(',', reservationIds.OrderBy(id => id, StringComparer.Ordinal))}"
            : $"{customerAccount}|{Guid.NewGuid():N}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return Convert.ToHexString(hash);
    }

    private static OrderRequest BuildOrder(
        string customerAccount,
        ShoppingCart cart,
        PayRequest request,
        IReadOnlyDictionary<string, decimal> unitPriceByItem,
        string correlationId,
        string idempotencyKey,
        IReadOnlyList<string> reservationIds,
        string currency)
    {
        var order = new OrderRequest
        {
            IdempotencyKey = idempotencyKey,
            CorrelationId = correlationId,
            Customer = new CustomerRef { CustomerAccount = customerAccount },
            Currency = currency,
            PaymentMethod = request.PaymentMethod == SettlementMethod.OnAccount ? "OnAccount" : "Card",
        };

        if (!string.IsNullOrWhiteSpace(request.PoNumber))
        {
            order.PurchaseOrderNumber = request.PoNumber.Trim();
        }

        order.ReservationIds = reservationIds.ToList();

        foreach (var line in cart.Lines)
        {
            // Carry the server-resolved contract unit price so writeback price-integrity (TDD §9)
            // compares a real locked price, not a placeholder.
            var unitPrice = unitPriceByItem.TryGetValue(line.ItemNumber, out var price) ? price : 0m;
            order.Lines.Add(new OrderLineInput
            {
                ItemNumber = line.ItemNumber,
                Quantity = (double)line.Quantity,
                Unit = "ea",
                Site = line.Site,
                RequestedShipDate = DateTimeOffset.UtcNow,
                LockedPrice = new Money { Amount = (double)unitPrice, Currency = currency },
            });
        }

        return order;
    }

    private static PricingResolveRequest BuildPricing(string customerAccount, ShoppingCart cart) =>
        new(customerAccount, cart.Lines.Select(line => new PricingResolveLine(line.ItemNumber, line.Quantity)).ToList());
}
