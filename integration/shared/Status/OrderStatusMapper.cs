using PartsPortal.Shared.Contracts.Middleware;

namespace PartsPortal.Shared.Status;

/// <summary>Maps the storefront order-status mirror onto the middleware order-status contract the BFF reads.</summary>
public static class OrderStatusMapper
{
    public static OrderStatusResponse ToResponse(OrderStatusView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var response = new OrderStatusResponse
        {
            OrderId = view.SalesOrderNumber,
            SalesOrderNumber = view.SalesOrderNumber,
            Status = MapStatus(view.Status),
            RemainingBackorder = view.RemainingBackorder is { } backorder ? (double)backorder : null,
            Message = BuildMessage(view.Status, view.RemainingBackorder),
            Fulfilments = new List<OrderFulfilment>(),
        };

        foreach (var fulfilment in view.Fulfilments)
        {
            var mapped = new OrderFulfilment
            {
                TrackingNumber = fulfilment.TrackingNumber,
                Lines = new List<OrderFulfilmentLine>(),
            };
            foreach (var line in fulfilment.Lines)
            {
                mapped.Lines.Add(new OrderFulfilmentLine { ItemNumber = line.ItemNumber, Quantity = (double)line.Quantity });
            }

            response.Fulfilments.Add(mapped);
        }

        return response;
    }

    public static OrderStatus MapStatus(StorefrontOrderStatus status) => status switch
    {
        StorefrontOrderStatus.Shipped or StorefrontOrderStatus.Invoiced => OrderStatus.Fulfilled,
        StorefrontOrderStatus.PartiallyShipped => OrderStatus.PartiallyFulfilled,
        // Terminal negative states must not collapse into "WrittenBack" (shown to customers as
        // "Processing"); they map to dedicated statuses (DR-018). Packed/Updated are genuinely
        // post-writeback/pre-ship, so WrittenBack ("Processing") is honest for them.
        StorefrontOrderStatus.Cancelled => OrderStatus.Cancelled,
        StorefrontOrderStatus.Returned => OrderStatus.Returned,
        _ => OrderStatus.WrittenBack,
    };

    // Customer-facing detail — never leak the raw enum name. No personal data.
    private static string BuildMessage(StorefrontOrderStatus status, decimal? remainingBackorder)
    {
        var baseMessage = status switch
        {
            StorefrontOrderStatus.Packed => "Packed and ready to ship",
            StorefrontOrderStatus.Shipped => "Shipped",
            StorefrontOrderStatus.PartiallyShipped => "Partially shipped",
            StorefrontOrderStatus.Invoiced => "Invoiced",
            StorefrontOrderStatus.Updated => "Order updated",
            StorefrontOrderStatus.Returned => "Returned",
            StorefrontOrderStatus.Cancelled => "Cancelled",
            _ => "Processing",
        };

        return remainingBackorder is > 0
            ? $"{baseMessage} · {remainingBackorder} on backorder"
            : baseMessage;
    }
}
