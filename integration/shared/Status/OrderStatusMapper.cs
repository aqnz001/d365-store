using PartsPortal.Shared.Contracts.Middleware;

namespace PartsPortal.Shared.Status;

/// <summary>Maps the storefront order-status mirror onto the middleware order-status contract the BFF reads.</summary>
public static class OrderStatusMapper
{
    public static OrderStatusResponse ToResponse(OrderStatusView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new OrderStatusResponse
        {
            OrderId = view.SalesOrderNumber,
            SalesOrderNumber = view.SalesOrderNumber,
            Status = MapStatus(view.Status),
            Message = view.RemainingBackorder is > 0
                ? $"{view.Status} · {view.RemainingBackorder} on backorder"
                : view.Status.ToString(),
        };
    }

    public static OrderStatus MapStatus(StorefrontOrderStatus status) => status switch
    {
        StorefrontOrderStatus.Shipped or StorefrontOrderStatus.Invoiced => OrderStatus.Fulfilled,
        StorefrontOrderStatus.PartiallyShipped => OrderStatus.PartiallyFulfilled,
        _ => OrderStatus.WrittenBack,
    };
}
