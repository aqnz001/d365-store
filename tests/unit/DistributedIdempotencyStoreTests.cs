using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PartsPortal.Shared.Idempotency;
using Xunit;

namespace PartsPortal.Tests.Unit;

/// <summary>
/// Phase-2 (DR-011) — the durable idempotency store over IDistributedCache. Exercised with an
/// in-memory distributed cache here; production uses Redis behind the same IDistributedCache.
/// </summary>
public class DistributedIdempotencyStoreTests
{
    private static DistributedIdempotencyStore NewStore() =>
        new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

    [Fact]
    public async Task Unseen_key_returns_null_then_records_and_reads_back()
    {
        var store = NewStore();

        Assert.Null(await store.TryGetAsync("idem-1"));
        await store.SetAsync("idem-1", "SO-1");
        Assert.Equal("SO-1", await store.TryGetAsync("idem-1"));
    }

    [Fact]
    public async Task Duplicate_set_keeps_the_first_result()
    {
        var store = NewStore();

        await store.SetAsync("idem-2", "SO-100");
        await store.SetAsync("idem-2", "SO-999"); // late duplicate must not clobber the original

        Assert.Equal("SO-100", await store.TryGetAsync("idem-2"));
    }
}
