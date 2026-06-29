using System.Collections.Concurrent;
using PartsPortal.Bff.Cart;

namespace PartsPortal.Bff.Checkout;

/// <summary>
/// The server-authoritative outcome of the checkout gate for a customer: the soft-reservation ids
/// placed at <c>/checkout/start</c> and the exact cart <see cref="Lines"/> they were placed for.
/// Payment reads these server-side rather than trusting the client (the gate is the only place
/// reservations are made — Golden Rule #5), anchors the idempotency key to the real reservation set
/// (DR-020), and rejects payment if the cart changed since the gate so an order can never carry
/// lines that were never reserved (DR-025).
/// </summary>
public sealed record CheckoutSession(IReadOnlyList<string> ReservationIds, IReadOnlyList<CartLine> Lines);

/// <summary>Server-side checkout-gate state per customer. Phase 1 in-memory; Phase 2 a durable
/// session store (same posture as the cart store and reservation registry, DR-011).</summary>
public interface ICheckoutSessionStore
{
    void Set(string customerAccount, CheckoutSession session);

    CheckoutSession? Get(string customerAccount);

    void Clear(string customerAccount);
}

/// <inheritdoc />
public sealed class InMemoryCheckoutSessionStore : ICheckoutSessionStore
{
    private readonly ConcurrentDictionary<string, CheckoutSession> _sessions = new(StringComparer.Ordinal);

    public void Set(string customerAccount, CheckoutSession session) => _sessions[customerAccount] = session;

    public CheckoutSession? Get(string customerAccount) =>
        _sessions.TryGetValue(customerAccount, out var session) ? session : null;

    public void Clear(string customerAccount) => _sessions.TryRemove(customerAccount, out _);
}
