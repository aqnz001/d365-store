using PartsPortal.Bff.Account;
using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Clients;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Pricing;

namespace PartsPortal.Bff.Payments;

/// <summary>Request to pay for the reserved cart (amount/currency are the locked checkout total).</summary>
public sealed record PayRequest(decimal Amount, string Currency, string PaymentToken, IReadOnlyList<string> ReservationIds);

/// <summary>Outcome of paying: OrderPlaced (with the order reference), PaymentFailed, or EmptyCart.</summary>
public sealed record PayResult(string Status, string? OrderReference, string? Message);

/// <summary>
/// Takes the payment for a checked-out cart, then submits the order for queue-backed writeback
/// (TDD §6.1→§6.2): authorize via the payment provider (DR-003), and only on success build the
/// order (carrying the reservation ids + idempotency key) and enqueue it via the middleware. The
/// cart is cleared on success. Checkout never blocks on the ERP (Golden Rule #7).
/// </summary>
public sealed class PaymentService(ICartStore cartStore, IPaymentProvider payments, IMiddlewareApi middleware, IOrderHistoryStore history)
{
    public async Task<PayResult> PayAsync(string customerAccount, PayRequest request, string correlationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cart = cartStore.Get(customerAccount);
        if (cart.Lines.Count == 0)
        {
            return new PayResult("EmptyCart", null, "Cart is empty.");
        }

        // Re-resolve contract pricing server-side — the charge amount and the order's locked prices
        // are authoritative here, never trusted from the client (the client-sent amount is ignored).
        var pricing = await middleware.ResolvePricingAsync(BuildPricing(customerAccount, cart), correlationId, ct);
        var unitPriceByItem = pricing.Lines
            .GroupBy(line => line.ItemNumber)
            .ToDictionary(group => group.Key, group => group.First().UnitPrice, StringComparer.Ordinal);
        var amount = pricing.Lines.Sum(line => line.NetEffectivePrice);

        var payment = await payments.AuthorizeAsync(
            new PaymentRequest(amount, request.Currency, request.PaymentToken, customerAccount), ct);
        if (payment.Status != PaymentStatus.Succeeded)
        {
            return new PayResult("PaymentFailed", null, payment.Message ?? payment.Status.ToString());
        }

        var ack = await middleware.SubmitOrderAsync(BuildOrder(customerAccount, cart, request, unitPriceByItem, correlationId), correlationId, ct);
        cartStore.Clear(customerAccount);

        var reference = ack.SalesOrderNumber ?? ack.OrderId ?? "pending";
        history.Record(customerAccount, new PlacedOrder(reference, DateTimeOffset.UtcNow));
        return new PayResult("OrderPlaced", reference, null);
    }

    private static OrderRequest BuildOrder(
        string customerAccount,
        ShoppingCart cart,
        PayRequest request,
        IReadOnlyDictionary<string, decimal> unitPriceByItem,
        string correlationId)
    {
        var order = new OrderRequest
        {
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            CorrelationId = correlationId,
            Customer = new CustomerRef { CustomerAccount = customerAccount },
            Currency = request.Currency,
        };

        order.ReservationIds = request.ReservationIds.ToList();

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
                LockedPrice = new Money { Amount = (double)unitPrice, Currency = request.Currency },
            });
        }

        return order;
    }

    private static PricingResolveRequest BuildPricing(string customerAccount, ShoppingCart cart) =>
        new(customerAccount, cart.Lines.Select(line => new PricingResolveLine(line.ItemNumber, line.Quantity)).ToList());
}
