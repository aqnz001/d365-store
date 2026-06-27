namespace PartsPortal.Shared.Http;

/// <summary>
/// Outbound auth for the external HTTP clients (Phase 2): real D365 OData and IVS require an Entra
/// bearer token (client-credentials or managed identity). Per-client scopes are config-driven and a
/// client with no scope sends no token — so the Phase-1 mocks keep working with zero auth config.
/// </summary>
public sealed class ExternalAuthOptions
{
    public const string SectionName = "ExternalAuth";

    /// <summary>Entra tenant (directory) id. Unused when <see cref="UseManagedIdentity"/> is true.</summary>
    public string? TenantId { get; set; }

    /// <summary>App registration (client) id used for the client-credentials flow.</summary>
    public string? ClientId { get; set; }

    /// <summary>App client secret (Key Vault — never in config). Omit to use a certificate / managed identity.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Use the app's managed identity (DefaultAzureCredential) instead of a client secret.</summary>
    public bool UseManagedIdentity { get; set; }

    /// <summary>
    /// Token scope/resource per external client name ("ivs" / "odata" / "pricing-credit" / "shopify").
    /// e.g. odata = "https://{d365-host}/.default". A client absent here gets no bearer token.
    /// </summary>
    public Dictionary<string, string> Scopes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
