using PartsPortal.Shared.Idempotency;
using Xunit;

namespace PartsPortal.Tests.Unit;

public sealed class InMemoryIdempotencyStoreTests
{
    [Fact]
    public async Task TryGetAsync_UnseenKey_ReturnsNull()
    {
        var store = new InMemoryIdempotencyStore();

        var result = await store.TryGetAsync("never-set");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_ThenTryGetAsync_ReturnsRecordedValue()
    {
        var store = new InMemoryIdempotencyStore();
        const string key = "idem-key-1";
        const string salesOrder = "SO-1001";

        await store.SetAsync(key, salesOrder);
        var result = await store.TryGetAsync(key);

        Assert.Equal(salesOrder, result);
    }

    [Fact]
    public async Task SetAsync_DuplicateKeyDifferentValue_KeepsFirst()
    {
        // De-dup invariant: a late duplicate must not clobber the original sales-order number.
        var store = new InMemoryIdempotencyStore();
        const string key = "idem-key-dup";

        await store.SetAsync(key, "SO-FIRST");
        await store.SetAsync(key, "SO-SECOND");

        var result = await store.TryGetAsync(key);
        Assert.Equal("SO-FIRST", result);
    }

    [Fact]
    public async Task SetAsync_SameKeyAndValueTwice_RemainsStable()
    {
        var store = new InMemoryIdempotencyStore();
        const string key = "idem-key-same";

        await store.SetAsync(key, "SO-2002");
        await store.SetAsync(key, "SO-2002");

        Assert.Equal("SO-2002", await store.TryGetAsync(key));
    }

    [Fact]
    public async Task DistinctKeys_AreStoredIndependently()
    {
        var store = new InMemoryIdempotencyStore();

        await store.SetAsync("key-a", "SO-A");
        await store.SetAsync("key-b", "SO-B");

        Assert.Equal("SO-A", await store.TryGetAsync("key-a"));
        Assert.Equal("SO-B", await store.TryGetAsync("key-b"));
    }

    [Fact]
    public async Task SetAsync_HighConcurrencySameKey_YieldsSingleStableResult()
    {
        // Many writers race on one key with distinct values; first-write-wins must pick exactly one,
        // and the chosen value must remain stable after all writers complete.
        var store = new InMemoryIdempotencyStore();
        const string key = "idem-key-race";
        const int writers = 256;

        var tasks = Enumerable.Range(0, writers)
            .Select(i => Task.Run(() => store.SetAsync(key, $"SO-{i}")))
            .ToArray();

        await Task.WhenAll(tasks);

        var winner = await store.TryGetAsync(key);
        Assert.NotNull(winner);

        // The stored value must be one of the candidates and must not change on re-read.
        var expected = Enumerable.Range(0, writers).Select(i => $"SO-{i}");
        Assert.Contains(winner, expected);
        Assert.Equal(winner, await store.TryGetAsync(key));
    }

    [Fact]
    public async Task TryGetAsync_NullKey_Throws()
    {
        var store = new InMemoryIdempotencyStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.TryGetAsync(null!));
    }

    [Fact]
    public async Task SetAsync_NullKeyOrResult_Throws()
    {
        var store = new InMemoryIdempotencyStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SetAsync(null!, "SO-1"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SetAsync("key", null!));
    }

    [Fact]
    public async Task TryGetAsync_CancelledToken_Throws()
    {
        var store = new InMemoryIdempotencyStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.TryGetAsync("key", cts.Token));
    }

    [Fact]
    public async Task SetAsync_CancelledToken_Throws()
    {
        var store = new InMemoryIdempotencyStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.SetAsync("key", "SO-1", cts.Token));
    }
}
