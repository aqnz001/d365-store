namespace PartsPortal.Bff.Account;

/// <summary>A saved shipping/billing address in the customer's account address book (#5).</summary>
public sealed record Address(
    string Id,
    string Name,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string Country,
    bool IsDefaultShipping,
    bool IsDefaultBilling);

/// <summary>What the client supplies to create/update an address (the id + default exclusivity are
/// owned server-side).</summary>
public sealed record AddressInput(
    string Name,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string Country,
    bool IsDefaultShipping = false,
    bool IsDefaultBilling = false);

/// <summary>Per-customer address book. Phase 1 in-memory (durable Phase 2, like the cart — DR-011).
/// Mutations go through <see cref="Mutate"/> so the read-modify-write is atomic per customer (a
/// double-click that adds two addresses can't lose one).</summary>
public interface IAddressStore
{
    IReadOnlyList<Address> List(string customerAccount);

    /// <summary>Atomically replaces the customer's address list: the mutator receives a working copy
    /// of the current list and returns the new list, all under a single lock.</summary>
    void Mutate(string customerAccount, Func<List<Address>, List<Address>> mutator);
}

/// <inheritdoc />
public sealed class InMemoryAddressStore : IAddressStore
{
    private readonly Dictionary<string, List<Address>> _byCustomer = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public IReadOnlyList<Address> List(string customerAccount)
    {
        lock (_gate)
        {
            return _byCustomer.TryGetValue(customerAccount, out var list) ? list.ToList() : [];
        }
    }

    public void Mutate(string customerAccount, Func<List<Address>, List<Address>> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        lock (_gate)
        {
            var current = _byCustomer.TryGetValue(customerAccount, out var list) ? list.ToList() : [];
            _byCustomer[customerAccount] = mutator(current);
        }
    }
}
