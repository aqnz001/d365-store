using Microsoft.Extensions.Configuration;

namespace PartsPortal.Shared.Notifications;

/// <summary>
/// Resolves where to send a customer's notifications. The customer's email is PII, so it is NEVER
/// carried in the order/status messages (Golden Rule) — it is resolved here from a contact source
/// keyed by the (non-PII) customer account. Phase 1 uses config; Phase 2 swaps in the FinOps
/// customer master / a notification-preferences store, behind this same interface.
/// </summary>
public interface INotificationContacts
{
    /// <summary>The email to notify for a customer account, or <c>null</c> when none is on record
    /// (the caller then skips the email rather than failing).</summary>
    string? ResolveEmail(string customerAccount);
}

/// <summary>
/// Config-backed contact resolver (Phase 1). Looks up an explicit per-customer address under
/// <c>Notifications:Contacts:{customerAccount}</c>, else synthesizes one from
/// <c>Notifications:DefaultEmailDomain</c> (a mock stand-in for the customer master), else null.
/// </summary>
public sealed class ConfigNotificationContacts(IConfiguration configuration) : INotificationContacts
{
    public string? ResolveEmail(string customerAccount)
    {
        if (string.IsNullOrWhiteSpace(customerAccount))
        {
            return null;
        }

        // An explicit per-customer address is operator-configured and trusted as-is.
        var explicitAddress = configuration[$"Notifications:Contacts:{customerAccount}"];
        if (!string.IsNullOrWhiteSpace(explicitAddress))
        {
            return explicitAddress;
        }

        var domain = configuration["Notifications:DefaultEmailDomain"];
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        // Only synthesize an address when the account code is a safe email local-part — otherwise a
        // stray account (spaces, @, <>, …) would produce a malformed recipient. Unknown → no email.
        return System.Text.RegularExpressions.Regex.IsMatch(customerAccount, "^[A-Za-z0-9._-]+$")
            ? $"{customerAccount.ToLowerInvariant()}@{domain}"
            : null;
    }
}
