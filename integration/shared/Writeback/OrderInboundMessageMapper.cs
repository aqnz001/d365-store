using PartsPortal.Shared.Contracts.Middleware;
using Msg = PartsPortal.Shared.Contracts.Messages;

namespace PartsPortal.Shared.Writeback;

/// <summary>
/// Maps the BFF's <see cref="OrderRequest"/> (middleware contract) to the
/// <see cref="Msg.OrderInboundMessage"/> envelope consumed by the writeback (TDD §5.5). Shared by
/// the production HTTP order-intake function and the dev-gateway so both build the identical
/// message — including the server-resolved locked price per line (so price-integrity, TDD §9,
/// compares a real value) and the session id (customer) for per-customer ordered writeback.
/// </summary>
public static class OrderInboundMessageMapper
{
    public static Msg.OrderInboundMessage ToInboundMessage(OrderRequest request, DateTimeOffset placedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        var message = new Msg.OrderInboundMessage
        {
            IdempotencyKey = request.IdempotencyKey,
            CorrelationId = request.CorrelationId,
            SessionId = request.Customer.CustomerAccount,
            CustomerAccount = request.Customer.CustomerAccount,
            Currency = request.Currency,
            // "Card" (prepaid) vs "OnAccount" (net terms) — defaults to Card when the order omits it.
            PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "Card" : request.PaymentMethod,
            PurchaseOrderNumber = string.IsNullOrWhiteSpace(request.PurchaseOrderNumber) ? null : request.PurchaseOrderNumber,
            PlacedAtUtc = placedAtUtc,
        };

        var reservations = request.ReservationIds.ToList();
        var index = 0;
        foreach (var line in request.Lines)
        {
            message.Lines.Add(new Msg.OrderLine
            {
                ItemNumber = line.ItemNumber,
                Quantity = line.Quantity,
                Unit = string.IsNullOrWhiteSpace(line.Unit) ? "ea" : line.Unit,
                Site = string.IsNullOrWhiteSpace(line.Site) ? "1" : line.Site,
                Backorder = line.Backorder,
                ReservationReference = index < reservations.Count ? reservations[index] : string.Empty,
                LockedPrice = new Msg.Money
                {
                    Amount = line.LockedPrice?.Amount ?? 0,
                    Currency = string.IsNullOrWhiteSpace(line.LockedPrice?.Currency) ? request.Currency : line.LockedPrice!.Currency,
                },
            });
            index++;
        }

        return message;
    }
}
