using System.Collections.Concurrent;

namespace PartsPortal.Bff.Cart;

/// <summary>Server-side cart state per customer. Phase 1 in-memory; Phase 2 a durable/session store.</summary>
public interface ICartStore
{
    ShoppingCart Get(string customerAccount);

    void Add(string customerAccount, CartLine line);

    void Clear(string customerAccount);
}

/// <inheritdoc />
public sealed class InMemoryCartStore : ICartStore
{
    private readonly ConcurrentDictionary<string, List<CartLine>> _carts = new(StringComparer.Ordinal);

    public ShoppingCart Get(string customerAccount) =>
        new(_carts.TryGetValue(customerAccount, out var lines) ? lines.ToList() : []);

    public void Add(string customerAccount, CartLine line) =>
        _carts.GetOrAdd(customerAccount, _ => []).Add(line);

    public void Clear(string customerAccount) => _carts.TryRemove(customerAccount, out _);
}
