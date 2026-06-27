using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PartsPortal.Shared.Contracts.Messages;
using PartsPortal.Shared.Status;
using Xunit;

namespace PartsPortal.Tests.Unit;

/// <summary>
/// Phase-2 durable order-status mirror over IDistributedCache (DR-011) + the in-process status
/// events emitter (T10): fulfilments accumulate across events and survive a fresh store instance.
/// </summary>
public class DistributedStatusStoreTests
{
    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public void Apply_accumulates_fulfilments_across_events()
    {
        var store = new DistributedOrderStatusStore(NewCache());

        store.Apply("SO-1", StorefrontOrderStatus.PartiallyShipped, [new Fulfilment("TRK-1", [new FulfilmentLine("ITEM-1", 2)])], 3m);
        var partial = store.Get("SO-1")!;
        Assert.Equal(StorefrontOrderStatus.PartiallyShipped, partial.Status);
        Assert.Equal(3m, partial.RemainingBackorder);
        Assert.Single(partial.Fulfilments);

        store.Apply("SO-1", StorefrontOrderStatus.Shipped, [new Fulfilment("TRK-2", [new FulfilmentLine("ITEM-1", 3)])], 0m);
        var complete = store.Get("SO-1")!;
        Assert.Equal(StorefrontOrderStatus.Shipped, complete.Status);
        Assert.Equal(["TRK-1", "TRK-2"], complete.Fulfilments.Select(f => f.TrackingNumber));
    }

    [Fact]
    public void Unknown_order_returns_null() =>
        Assert.Null(new DistributedOrderStatusStore(NewCache()).Get("nope"));

    [Fact]
    public void InProcess_publisher_applies_events_to_the_store()
    {
        var cache = NewCache();
        var store = new DistributedOrderStatusStore(cache);
        var sync = new StatusSyncService(store, NullLogger<StatusSyncService>.Instance);
        var publisher = new InProcessStatusEventPublisher(sync);

        var shipment = new Shipment { TrackingNumber = "TRK-9" };
        shipment.Lines.Add(new ShipmentLine { ItemNumber = "ITEM-1", Quantity = 1 });
        publisher.PublishAsync(new FulfilmentStatusEvent
        {
            SalesOrderNumber = "SO-PUB",
            EventType = FulfilmentStatusEventEventType.Shipped,
            Shipments = [shipment],
            RemainingBackorder = 0,
            CorrelationId = "corr",
            OccurredAtUtc = new DateTimeOffset(2026, 6, 27, 9, 0, 0, TimeSpan.Zero),
        }).GetAwaiter().GetResult();

        Assert.Equal(StorefrontOrderStatus.Shipped, store.Get("SO-PUB")!.Status);
    }
}
