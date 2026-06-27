using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PartsPortal.Shared.Reservations;
using Xunit;

namespace PartsPortal.Tests.Unit;

/// <summary>
/// Phase-2 durable reservation registry over IDistributedCache (DR-011): the index-backed store
/// must enumerate stale soft reservations and stop returning them once converted/released.
/// </summary>
public class DistributedReservationRegistryTests
{
    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static readonly DateTimeOffset Old = new(2026, 6, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Recent = new(2026, 6, 27, 9, 14, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = new(2026, 6, 27, 9, 10, 0, TimeSpan.Zero);

    [Fact]
    public void FindStaleSoft_returns_only_soft_reservations_older_than_cutoff()
    {
        var registry = new DistributedReservationRegistry(NewCache());
        registry.Record("RSV-old", Old, "corr-1");
        registry.Record("RSV-new", Recent, "corr-2");

        var stale = registry.FindStaleSoft(Cutoff);

        var entry = Assert.Single(stale);
        Assert.Equal("RSV-old", entry.ReservationId);
        Assert.Equal(ReservationState.Soft, entry.State);
        Assert.Equal("corr-1", entry.CorrelationId);
    }

    [Fact]
    public void Released_reservation_is_no_longer_stale()
    {
        var registry = new DistributedReservationRegistry(NewCache());
        registry.Record("RSV-1", Old, "corr");
        Assert.Single(registry.FindStaleSoft(Cutoff));

        registry.MarkReleased("RSV-1");

        Assert.Empty(registry.FindStaleSoft(Cutoff));
    }

    [Fact]
    public void Converted_reservation_is_not_swept()
    {
        var registry = new DistributedReservationRegistry(NewCache());
        registry.Record("RSV-2", Old, "corr");

        registry.MarkConverted("RSV-2");

        Assert.Empty(registry.FindStaleSoft(Cutoff));
    }

    [Fact]
    public void Shared_cache_is_visible_to_a_second_registry_instance()
    {
        // The whole point of DR-011: a separate process (here, a second instance over the same
        // backend) sees reservations placed by the first.
        var cache = NewCache();
        new DistributedReservationRegistry(cache).Record("RSV-shared", Old, "corr");

        var sweeper = new DistributedReservationRegistry(cache);
        Assert.Equal("RSV-shared", Assert.Single(sweeper.FindStaleSoft(Cutoff)).ReservationId);
    }
}
