using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Status;
using PartsPortal.Shared.Writeback;
using Xunit;

namespace PartsPortal.Tests.Unit;

/// <summary>
/// The shared order mappers used by both the production HTTP functions and the dev-gateway:
/// OrderRequest → OrderInboundMessage (carries the server-resolved locked price + session id), and
/// the storefront status view → the middleware order-status contract.
/// </summary>
public class OrderMappingTests
{
    [Fact]
    public void ToInboundMessage_carries_locked_price_session_and_reservation()
    {
        var request = new OrderRequest
        {
            IdempotencyKey = "idem-1",
            CorrelationId = "corr-1",
            Customer = new CustomerRef { CustomerAccount = "C-1" },
            Currency = "GBP",
            ReservationIds = new List<string> { "RSV-1" },
        };
        request.Lines.Add(new OrderLineInput
        {
            ItemNumber = "PART-1",
            Quantity = 3,
            Unit = "ea",
            Site = "1",
            Backorder = false,
            LockedPrice = new Money { Amount = 12.50, Currency = "GBP" },
        });

        var message = OrderInboundMessageMapper.ToInboundMessage(request, new DateTimeOffset(2026, 6, 27, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal("idem-1", message.IdempotencyKey);
        Assert.Equal("C-1", message.SessionId); // per-customer ordered writeback
        var line = Assert.Single(message.Lines);
        Assert.Equal("PART-1", line.ItemNumber);
        Assert.Equal(12.50, line.LockedPrice.Amount, 3); // real locked price, not a placeholder
        Assert.Equal("RSV-1", line.ReservationReference);
    }

    [Theory]
    [InlineData(StorefrontOrderStatus.Shipped, OrderStatus.Fulfilled)]
    [InlineData(StorefrontOrderStatus.Invoiced, OrderStatus.Fulfilled)]
    [InlineData(StorefrontOrderStatus.PartiallyShipped, OrderStatus.PartiallyFulfilled)]
    [InlineData(StorefrontOrderStatus.Packed, OrderStatus.WrittenBack)]
    // Terminal negative states must NOT collapse into WrittenBack ("Processing") — DR-018.
    [InlineData(StorefrontOrderStatus.Cancelled, OrderStatus.Cancelled)]
    [InlineData(StorefrontOrderStatus.Returned, OrderStatus.Returned)]
    public void MapStatus_maps_storefront_status_to_order_status(StorefrontOrderStatus input, OrderStatus expected)
        => Assert.Equal(expected, OrderStatusMapper.MapStatus(input));

    [Fact]
    public void ToResponse_does_not_show_cancelled_order_as_processing()
    {
        var view = new OrderStatusView("SO-3", StorefrontOrderStatus.Cancelled, [], RemainingBackorder: null);
        var response = OrderStatusMapper.ToResponse(view);

        Assert.Equal(OrderStatus.Cancelled, response.Status);
        Assert.Equal("Cancelled", response.Message);
    }

    [Fact]
    public void ToResponse_message_never_leaks_raw_enum_name()
    {
        var view = new OrderStatusView("SO-4", StorefrontOrderStatus.PartiallyShipped, [], 3m);
        var response = OrderStatusMapper.ToResponse(view);

        // Sentence-cased customer phrase, not the "PartiallyShipped" enum token.
        Assert.Equal("Partially shipped · 3 on backorder", response.Message);
        Assert.DoesNotContain("PartiallyShipped", response.Message);
    }

    [Fact]
    public void ToResponse_surfaces_remaining_backorder()
    {
        var view = new OrderStatusView("SO-1", StorefrontOrderStatus.PartiallyShipped, [], 3m);
        var response = OrderStatusMapper.ToResponse(view);

        Assert.Equal("SO-1", response.SalesOrderNumber);
        Assert.Equal(OrderStatus.PartiallyFulfilled, response.Status);
        Assert.Contains("3 on backorder", response.Message);
    }

    [Fact]
    public void ToResponse_maps_fulfilments_with_tracking_and_lines()
    {
        var view = new OrderStatusView(
            "SO-2",
            StorefrontOrderStatus.Shipped,
            [new Fulfilment("TRACK-99", [new FulfilmentLine("PART-1", 4m), new FulfilmentLine("PART-2", 2m)])],
            RemainingBackorder: null);

        var response = OrderStatusMapper.ToResponse(view);

        var fulfilment = Assert.Single(response.Fulfilments);
        Assert.Equal("TRACK-99", fulfilment.TrackingNumber);
        Assert.Collection(
            fulfilment.Lines,
            l => { Assert.Equal("PART-1", l.ItemNumber); Assert.Equal(4d, l.Quantity); },
            l => { Assert.Equal("PART-2", l.ItemNumber); Assert.Equal(2d, l.Quantity); });
        Assert.Null(response.RemainingBackorder);
    }
}
