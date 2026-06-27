using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PartsPortal.Bff.Account;
using PartsPortal.Bff.Cart;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// Phase-2 durable BFF stores over IDistributedCache (DR-011): the cart and order history survive
/// a BFF restart / scale-out (modelled here as a second store instance over the same backend).
/// </summary>
public class DistributedBffStoresTests
{
    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public void Cart_add_remove_clear_round_trips_through_the_cache()
    {
        var store = new DistributedCartStore(NewCache());

        Assert.Empty(store.Get("C-1").Lines);
        store.Add("C-1", new CartLine("PART-1", 2, "1"));
        store.Add("C-1", new CartLine("PART-2", 6, "1"));
        Assert.Equal(2, store.Get("C-1").Lines.Count);

        store.RemoveAt("C-1", 0);
        var line = Assert.Single(store.Get("C-1").Lines);
        Assert.Equal("PART-2", line.ItemNumber);

        store.Clear("C-1");
        Assert.Empty(store.Get("C-1").Lines);
    }

    [Fact]
    public void Cart_is_visible_to_a_second_store_instance()
    {
        var cache = NewCache();
        new DistributedCartStore(cache).Add("C-2", new CartLine("PART-9", 1, "1"));

        var afterRestart = new DistributedCartStore(cache);
        Assert.Equal("PART-9", Assert.Single(afterRestart.Get("C-2").Lines).ItemNumber);
    }

    [Fact]
    public void Order_history_records_and_survives_a_new_instance()
    {
        var cache = NewCache();
        var placed = new DateTimeOffset(2026, 6, 27, 9, 0, 0, TimeSpan.Zero);
        new DistributedOrderHistoryStore(cache).Record("C-3", new PlacedOrder("SO-000001", placed));

        var afterRestart = new DistributedOrderHistoryStore(cache);
        var order = Assert.Single(afterRestart.GetOrders("C-3"));
        Assert.Equal("SO-000001", order.OrderReference);
    }
}
