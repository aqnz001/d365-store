using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Clients;
using PartsPortal.Shared.Contracts.Middleware;

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
public sealed class PaymentService(ICartStore cartStore, IPaymentProvider payments, IMiddlewareApi middleware)
{
    public async Task<PayResult> PayAsync(string customerAccount, PayRequest request, string correlationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cart = cartStore.Get(customerAccount);
        if (cart.Lines.Count == 0)
        {
            return new PayResult("EmptyCart", null, "Cart is empty.");
        }

        var payment = await payments.AuthorizeAsync(
            new PaymentRequest(request.Amount, request.Currency, request.PaymentToken, customerAccount), ct);
        if (payment.Status != PaymentStatus.Succeeded)
        {
            return new PayResult("PaymentFailed", null, payment.Message ?? payment.Status.ToString());
        }

        var ack = await middleware.SubmitOrderAsync(BuildOrder(customerAccount, cart, request, correlationId), correlationId, ct);
        cartStore.Clear(customerAccount);
        return new PayResult("OrderPlaced", ack.SalesOrderNumber ?? ack.OrderId, null);
    }

    private static OrderRequest BuildOrder(string customerAccount, ShoppingCart cart, PayRequest request, string correlationId)
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
            order.Lines.Add(new OrderLineInput { ItemNumber = line.ItemNumber, Quantity = (double)line.Quantity, Site = line.Site });
        }

        return order;
    }
}
