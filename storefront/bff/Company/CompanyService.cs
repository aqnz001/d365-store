namespace PartsPortal.Bff.Company;

/// <summary>Outcome of adding/updating a member.</summary>
public sealed record MemberResult(bool Ok, string? Error, CompanyMember? Member);

/// <summary>Outcome of removing a member.</summary>
public enum RemoveOutcome
{
    Removed,
    NotFound,
    LastAdminBlocked,
}

/// <summary>
/// Company member directory + roles (B2B governance, #7 / DR-026). Resolves a user's role, and lets
/// an admin manage the team. Invariant: a company always keeps at least one Admin, so it can never
/// be locked out of management.
/// </summary>
public sealed class CompanyService(ICompanyStore store)
{
    public IReadOnlyList<CompanyMember> Members(string companyAccount) => store.List(companyAccount);

    /// <summary>A user's role (no side effects): a member's role; else Admin for the first user of an
    /// empty company (bootstrap); else Buyer.</summary>
    public CompanyRole ResolveRole(string companyAccount, string userId)
    {
        var members = store.List(companyAccount);
        var me = members.FirstOrDefault(m => string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase));
        if (me is not null)
        {
            return me.Role;
        }

        return members.Count == 0 ? CompanyRole.Admin : CompanyRole.Buyer;
    }

    public bool IsAdmin(string companyAccount, string userId) => ResolveRole(companyAccount, userId) == CompanyRole.Admin;

    /// <summary>True when this user may decide approvals (Approver or Admin) — DR-027.</summary>
    public bool CanApprove(string companyAccount, string userId) => ResolveRole(companyAccount, userId) >= CompanyRole.Approver;

    /// <summary>The on-account order value above which this user's orders route to approval, or null if
    /// they are unrestricted — Approvers/Admins, non-members, and Buyers with no spend limit set all
    /// place orders directly (DR-027).</summary>
    public decimal? ApprovalThreshold(string companyAccount, string userId)
    {
        var member = store.List(companyAccount)
            .FirstOrDefault(m => string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase));
        return member is { Role: CompanyRole.Buyer } ? member.SpendLimit : null;
    }

    /// <summary>Adds or updates a member. The caller (an admin) is persisted as Admin when they act on
    /// an empty company, so they don't lose the implicit bootstrap admin on the next request.</summary>
    public MemberResult Save(string companyAccount, string callerUserId, string callerName, CompanyMemberInput input)
    {
        if (Validate(input) is { } error)
        {
            return new MemberResult(false, error, null);
        }

        MemberResult? result = null;
        store.Mutate(companyAccount, list =>
        {
            if (list.Count == 0 && !string.Equals(callerUserId, input.UserId, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new CompanyMember(callerUserId, callerName, CompanyRole.Admin, null));
            }

            var member = new CompanyMember(input.UserId.Trim(), input.Name.Trim(), input.Role, input.SpendLimit);
            var index = list.FindIndex(m => string.Equals(m.UserId, input.UserId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                list[index] = member;
            }
            else
            {
                list.Add(member);
            }

            EnsureAdmin(list, callerUserId);
            var saved = list.FirstOrDefault(m => string.Equals(m.UserId, input.UserId, StringComparison.OrdinalIgnoreCase));
            result = saved is null
                ? new MemberResult(false, "Failed to save the member.", null)
                : new MemberResult(true, null, saved);
            return list;
        });
        return result!;
    }

    public RemoveOutcome Remove(string companyAccount, string userId)
    {
        var outcome = RemoveOutcome.NotFound;
        store.Mutate(companyAccount, list =>
        {
            var target = list.FirstOrDefault(m => string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return list;
            }

            if (target.Role == CompanyRole.Admin && list.Count(m => m.Role == CompanyRole.Admin) <= 1)
            {
                outcome = RemoveOutcome.LastAdminBlocked;
                return list;
            }

            list.RemoveAll(m => string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase));
            outcome = RemoveOutcome.Removed;
            return list;
        });
        return outcome;
    }

    // Guarantee at least one Admin remains (e.g. an admin can't demote the last admin to Buyer).
    private static void EnsureAdmin(List<CompanyMember> list, string callerUserId)
    {
        if (list.Count == 0 || list.Any(m => m.Role == CompanyRole.Admin))
        {
            return;
        }

        var index = list.FindIndex(m => string.Equals(m.UserId, callerUserId, StringComparison.OrdinalIgnoreCase));
        index = index >= 0 ? index : 0;
        list[index] = list[index] with { Role = CompanyRole.Admin };
    }

    private static string? Validate(CompanyMemberInput input)
    {
        if (input is null)
        {
            return "Member is required.";
        }

        if (string.IsNullOrWhiteSpace(input.UserId))
        {
            return "User email is required.";
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return "Name is required.";
        }

        if (input.SpendLimit is < 0)
        {
            return "Spend limit cannot be negative.";
        }

        return null;
    }
}
