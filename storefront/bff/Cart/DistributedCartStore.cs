using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace PartsPortal.Bff.Cart;

/// <summary>
/// Durable server-side cart over <see cref="IDistributedCache"/> (Phase 2, DR-011): keyed by
/// customer account so the cart survives BFF instance restarts / scale-out (replaces the
/// in-memory store). Tokens/sensitive data still never touch the browser (Golden Rule #11).
/// </summary>
public sealed class DistributedCartStore(IDistributedCache cache) : ICartStore
{
    private const string Prefix = "cart:";

    public ShoppingCart Get(string customerAccount) => new(Read(customerAccount));

    public void Add(string customerAccount, CartLine line)
    {
        var lines = Read(customerAccount);
        lines.Add(line);
        Write(customerAccount, lines);
    }

    public void RemoveAt(string customerAccount, int index)
    {
        var lines = Read(customerAccount);
        if (index >= 0 && index < lines.Count)
        {
            lines.RemoveAt(index);
            Write(customerAccount, lines);
        }
    }

    public void Clear(string customerAccount) => cache.Remove(Prefix + customerAccount);

    private List<CartLine> Read(string customerAccount)
    {
        var json = cache.GetString(Prefix + customerAccount);
        return json is null ? [] : JsonSerializer.Deserialize<List<CartLine>>(json) ?? [];
    }

    private void Write(string customerAccount, List<CartLine> lines) =>
        cache.SetString(Prefix + customerAccount, JsonSerializer.Serialize(lines));
}
