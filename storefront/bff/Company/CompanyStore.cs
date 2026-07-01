using System.Text.Json.Serialization;

namespace PartsPortal.Bff.Company;

/// <summary>A member's role within their company (B2B governance, #7 / DR-026). Capabilities are
/// hierarchical: Admin ⊇ Approver ⊇ Buyer.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompanyRole>))]
public enum CompanyRole
{
    /// <summary>Can place orders (subject to their spend limit / approval).</summary>
    Buyer,

    /// <summary>Buyer + can approve orders that need approval.</summary>
    Approver,

    /// <summary>Approver + can manage the company's members and roles.</summary>
    Admin,
}

/// <summary>A user in a company's directory. <see cref="UserId"/> is the stable user identifier
/// (their email / Entra principal); a member's role + optional spend limit drive governance.</summary>
public sealed record CompanyMember(string UserId, string Name, CompanyRole Role, decimal? SpendLimit);

/// <summary>What an admin supplies to add/update a member (the company is the caller's; the id is
/// the member's email).</summary>
public sealed record CompanyMemberInput(string UserId, string Name, CompanyRole Role, decimal? SpendLimit);

/// <summary>Per-company member directory. Phase 1 in-memory (durable Phase 2, like the cart —
/// DR-011); mutations are atomic per company so concurrent admin edits can't lose one.</summary>
public interface ICompanyStore
{
    IReadOnlyList<CompanyMember> List(string companyAccount);

    void Mutate(string companyAccount, Func<List<CompanyMember>, List<CompanyMember>> mutator);
}

/// <inheritdoc />
public sealed class InMemoryCompanyStore : ICompanyStore
{
    private readonly Dictionary<string, List<CompanyMember>> _byCompany = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public IReadOnlyList<CompanyMember> List(string companyAccount)
    {
        lock (_gate)
        {
            return _byCompany.TryGetValue(companyAccount, out var list) ? list.ToList() : [];
        }
    }

    public void Mutate(string companyAccount, Func<List<CompanyMember>, List<CompanyMember>> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        lock (_gate)
        {
            var current = _byCompany.TryGetValue(companyAccount, out var list) ? list.ToList() : [];
            _byCompany[companyAccount] = mutator(current);
        }
    }
}
