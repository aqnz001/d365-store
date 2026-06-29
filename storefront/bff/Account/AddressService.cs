namespace PartsPortal.Bff.Account;

/// <summary>Outcome of an address mutation: the saved address, or a validation error / not-found.</summary>
public sealed record AddressResult(bool Ok, string? Error, Address? Address);

/// <summary>
/// Account address book CRUD (#5). Validates required fields and keeps exactly one default shipping
/// and one default billing address per customer (the first address becomes default for both).
/// </summary>
public sealed class AddressService(IAddressStore store)
{
    public IReadOnlyList<Address> List(string customerAccount) => store.List(customerAccount);

    public AddressResult Add(string customerAccount, AddressInput input)
    {
        if (Validate(input) is { } error)
        {
            return new AddressResult(false, error, null);
        }

        var id = Guid.NewGuid().ToString("N");
        Address? created = null;
        store.Mutate(customerAccount, list =>
        {
            list.Add(ToAddress(id, input));
            var normalized = Normalize(list, id, input.IsDefaultShipping, input.IsDefaultBilling);
            created = normalized.First(a => a.Id == id);
            return normalized;
        });
        return new AddressResult(true, null, created);
    }

    public AddressResult Update(string customerAccount, string id, AddressInput input)
    {
        if (Validate(input) is { } error)
        {
            return new AddressResult(false, error, null);
        }

        Address? updated = null;
        store.Mutate(customerAccount, list =>
        {
            var index = list.FindIndex(a => a.Id == id);
            if (index < 0)
            {
                return list; // not found — leave unchanged
            }

            list[index] = ToAddress(id, input);
            var normalized = Normalize(list, id, input.IsDefaultShipping, input.IsDefaultBilling);
            updated = normalized.First(a => a.Id == id);
            return normalized;
        });
        return updated is null ? new AddressResult(false, "Address not found.", null) : new AddressResult(true, null, updated);
    }

    public bool Remove(string customerAccount, string id)
    {
        var removed = false;
        store.Mutate(customerAccount, list =>
        {
            if (list.RemoveAll(a => a.Id == id) == 0)
            {
                return list;
            }

            removed = true;
            return EnsureDefaults(list);
        });
        return removed;
    }

    private static Address ToAddress(string id, AddressInput i) => new(
        id,
        i.Name.Trim(),
        i.Line1.Trim(),
        string.IsNullOrWhiteSpace(i.Line2) ? null : i.Line2.Trim(),
        i.City.Trim(),
        string.IsNullOrWhiteSpace(i.Region) ? null : i.Region.Trim(),
        i.PostalCode.Trim(),
        i.Country.Trim(),
        i.IsDefaultShipping,
        i.IsDefaultBilling);

    private static string? Validate(AddressInput i)
    {
        if (i is null)
        {
            return "Address is required.";
        }

        if (string.IsNullOrWhiteSpace(i.Name))
        {
            return "Contact name is required.";
        }

        if (string.IsNullOrWhiteSpace(i.Line1))
        {
            return "Address line 1 is required.";
        }

        if (string.IsNullOrWhiteSpace(i.City))
        {
            return "City is required.";
        }

        if (string.IsNullOrWhiteSpace(i.PostalCode))
        {
            return "Postal code is required.";
        }

        if (string.IsNullOrWhiteSpace(i.Country))
        {
            return "Country is required.";
        }

        return null;
    }

    /// <summary>The changed address's default flags are authoritative; a true flag clears that flag
    /// on every other address. Then guarantee exactly one default of each kind exists.</summary>
    private static List<Address> Normalize(List<Address> addresses, string changedId, bool defaultShipping, bool defaultBilling)
    {
        var list = addresses.Select(a => a.Id == changedId
            ? a with { IsDefaultShipping = defaultShipping, IsDefaultBilling = defaultBilling }
            : a with
            {
                IsDefaultShipping = defaultShipping ? false : a.IsDefaultShipping,
                IsDefaultBilling = defaultBilling ? false : a.IsDefaultBilling,
            }).ToList();
        return EnsureDefaults(list);
    }

    private static List<Address> EnsureDefaults(List<Address> addresses)
    {
        if (addresses.Count == 0)
        {
            return addresses;
        }

        if (!addresses.Any(a => a.IsDefaultShipping))
        {
            addresses[0] = addresses[0] with { IsDefaultShipping = true };
        }

        if (!addresses.Any(a => a.IsDefaultBilling))
        {
            addresses[0] = addresses[0] with { IsDefaultBilling = true };
        }

        return addresses;
    }
}
