using Microsoft.Extensions.Logging.Abstractions;
using PartsPortal.Shared.Contracts.Messages;
using PartsPortal.Shared.Notifications;
using PartsPortal.Shared.Status;
using Xunit;

namespace PartsPortal.Tests.Unit;

/// <summary>
/// T10 — fulfilment status events → storefront order-status mirror (TDD §6.3): event-type
/// mapping, multiple shipments accumulating into multiple fulfilments, remaining backorder.
/// </summary>
public class StatusSyncServiceTests
{
    [Theory]
    [InlineData(FulfilmentStatusEventEventType.Packed, StorefrontOrderStatus.Packed)]
    [InlineData(FulfilmentStatusEventEventType.Invoiced, StorefrontOrderStatus.Invoiced)]
    [InlineData(FulfilmentStatusEventEventType.StatusChanged, StorefrontOrderStatus.Updated)]
    [InlineData(FulfilmentStatusEventEventType.Shipped, StorefrontOrderStatus.Shipped)]
    public void MapStatus_without_backorder(FulfilmentStatusEventEventType eventType, StorefrontOrderStatus expected)
        => Assert.Equal(expected, StatusSyncService.MapStatus(eventType, remainingBackorder: null));

    [Fact]
    public void Shipped_with_remaining_backorder_is_partially_shipped()
        => Assert.Equal(StorefrontOrderStatus.PartiallyShipped, StatusSyncService.MapStatus(FulfilmentStatusEventEventType.Shipped, 3m));

    [Fact]
    public async Task Multiple_shipments_accumulate_into_fulfilments_with_backorder_then_complete()
    {
        var store = new InMemoryOrderStatusStore();
        var service = Build(store);

        await service.ApplyAsync(ShipEvent("SO-1", "TRACK-1", "ITEM-1", 2, remainingBackorder: 3));

        var afterFirst = store.Get("SO-1")!;
        Assert.Equal(StorefrontOrderStatus.PartiallyShipped, afterFirst.Status);
        Assert.Single(afterFirst.Fulfilments);
        Assert.Equal("TRACK-1", afterFirst.Fulfilments[0].TrackingNumber);
        Assert.Equal(3m, afterFirst.RemainingBackorder);

        await service.ApplyAsync(ShipEvent("SO-1", "TRACK-2", "ITEM-1", 3, remainingBackorder: 0));

        var afterSecond = store.Get("SO-1")!;
        Assert.Equal(StorefrontOrderStatus.Shipped, afterSecond.Status);
        Assert.Equal(2, afterSecond.Fulfilments.Count); // one order, two shipments
        Assert.Equal(["TRACK-1", "TRACK-2"], afterSecond.Fulfilments.Select(f => f.TrackingNumber));
        Assert.Equal(0m, afterSecond.RemainingBackorder);
    }

    [Fact]
    public async Task Shipment_sends_a_tracking_email_resolved_from_the_customer_master()
    {
        var store = new InMemoryOrderStatusStore();
        var email = new CapturingEmailSender();
        var service = Build(store, new FixedContacts("depot@acme.example"), email);

        var shipEvent = ShipEvent("SO-9", "TRACK-Z", "ITEM-1", 4, remainingBackorder: 0);
        shipEvent.CustomerAccount = "C-1"; // non-PII account routes the notification

        await service.ApplyAsync(shipEvent);

        var sent = Assert.Single(email.Sent);
        Assert.Equal("depot@acme.example", sent.To); // resolved from contacts, never from the event
        Assert.Contains("SO-9", sent.Subject);
        Assert.Contains("TRACK-Z", sent.Body);
    }

    [Fact]
    public async Task No_shipment_email_when_no_contact_is_on_record()
    {
        var store = new InMemoryOrderStatusStore();
        var email = new CapturingEmailSender();
        var service = Build(store, new FixedContacts(null), email); // unknown contact → no email

        await service.ApplyAsync(ShipEvent("SO-8", "TRACK-Y", "ITEM-1", 1, remainingBackorder: 0));

        Assert.Empty(email.Sent);
    }

    [Theory]
    [InlineData(FulfilmentStatusEventEventType.Packed)]
    [InlineData(FulfilmentStatusEventEventType.Invoiced)]
    [InlineData(FulfilmentStatusEventEventType.StatusChanged)]
    [InlineData(FulfilmentStatusEventEventType.Returned)]
    [InlineData(FulfilmentStatusEventEventType.Cancelled)]
    public async Task No_shipment_email_for_non_shipment_events(FulfilmentStatusEventEventType eventType)
    {
        var store = new InMemoryOrderStatusStore();
        var email = new CapturingEmailSender();
        var service = Build(store, new FixedContacts("depot@acme.example"), email); // contact exists

        await service.ApplyAsync(new FulfilmentStatusEvent
        {
            SalesOrderNumber = "SO-NE",
            CustomerAccount = "C-1",
            EventType = eventType,
            CorrelationId = "corr",
            OccurredAtUtc = new DateTimeOffset(2026, 6, 30, 9, 0, 0, TimeSpan.Zero),
        });

        Assert.Empty(email.Sent); // shipment email fires only on Shipped / PartiallyShipped
    }

    private static StatusSyncService Build(IOrderStatusStore store, INotificationContacts? contacts = null, IEmailSender? sender = null) =>
        new(store, contacts ?? new FixedContacts(null), sender ?? new CapturingEmailSender(), NullLogger<StatusSyncService>.Instance);

    private sealed class FixedContacts(string? email) : INotificationContacts
    {
        public string? ResolveEmail(string customerAccount) => email;
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static FulfilmentStatusEvent ShipEvent(string salesOrderNumber, string tracking, string item, double quantity, double remainingBackorder)
    {
        var shipment = new Shipment { TrackingNumber = tracking };
        shipment.Lines.Add(new ShipmentLine { ItemNumber = item, Quantity = quantity });
        return new FulfilmentStatusEvent
        {
            SalesOrderNumber = salesOrderNumber,
            EventType = FulfilmentStatusEventEventType.Shipped,
            Shipments = [shipment],
            RemainingBackorder = remainingBackorder,
            CorrelationId = "corr",
            OccurredAtUtc = new DateTimeOffset(2026, 6, 26, 9, 0, 0, TimeSpan.Zero),
        };
    }
}
