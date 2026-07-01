using PartsPortal.Bff.Cart;

namespace PartsPortal.Bff.Company;

/// <summary>Status of a pending-approval order (DR-027).</summary>
public static class ApprovalStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// <summary>
/// An on-account order that exceeded the buyer's spend limit and is waiting for an approver. It
/// snapshots the cart lines it was placed for (it holds NO reservation — approval re-reserves), plus
/// the buyer, amount and PO. No card data (on-account only).
/// </summary>
public sealed record PendingApproval(
    string Id,
    string BuyerUserId,
    string BuyerName,
    decimal Amount,
    string Currency,
    string? PoNumber,
    IReadOnlyList<CartLine> Lines,
    DateTimeOffset PlacedAtUtc,
    string Status,
    string? DecidedBy,
    string? OrderReference);

/// <summary>Per-company approval queue. Phase 1 in-memory (durable Phase 2); mutations are atomic.</summary>
public interface IApprovalStore
{
    IReadOnlyList<PendingApproval> List(string companyAccount);

    void Add(string companyAccount, PendingApproval approval);

    /// <summary>Atomically transitions one approval (approve/reject); the mutator returns the updated
    /// record or null to leave it unchanged. Returns the resulting record, or null if not found.</summary>
    PendingApproval? Transition(string companyAccount, string id, Func<PendingApproval, PendingApproval?> mutator);
}

/// <inheritdoc />
public sealed class InMemoryApprovalStore : IApprovalStore
{
    private readonly Dictionary<string, List<PendingApproval>> _byCompany = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public IReadOnlyList<PendingApproval> List(string companyAccount)
    {
        lock (_gate)
        {
            return _byCompany.TryGetValue(companyAccount, out var list) ? list.ToList() : [];
        }
    }

    public void Add(string companyAccount, PendingApproval approval)
    {
        lock (_gate)
        {
            if (!_byCompany.TryGetValue(companyAccount, out var list))
            {
                list = [];
                _byCompany[companyAccount] = list;
            }

            list.Add(approval);
        }
    }

    public PendingApproval? Transition(string companyAccount, string id, Func<PendingApproval, PendingApproval?> mutator)
    {
        lock (_gate)
        {
            if (!_byCompany.TryGetValue(companyAccount, out var list))
            {
                return null;
            }

            var index = list.FindIndex(a => a.Id == id);
            if (index < 0)
            {
                return null;
            }

            var updated = mutator(list[index]);
            if (updated is not null)
            {
                list[index] = updated;
            }

            return list[index];
        }
    }
}
