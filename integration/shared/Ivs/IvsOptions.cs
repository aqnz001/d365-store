namespace PartsPortal.Shared.Ivs;

/// <summary>
/// Config-driven IVS settings (Golden Rule #1). Endpoint base URL lives in
/// <see cref="PartsPortal.Shared.Http.ExternalEndpointOptions"/>; this carries the
/// environment id and the storefront dimension defaults.
/// </summary>
public sealed class IvsOptions
{
    public const string SectionName = "Ivs";

    /// <summary>IVS environment id used in the API path.</summary>
    public string EnvironmentId { get; set; } = "usmf";

    /// <summary>Default inventory location/warehouse (multi-warehouse = Open Decision #7).</summary>
    public string DefaultLocation { get; set; } = string.Empty;

    /// <summary>Soft-reservation TTL echoed to the client (Open Decision #4 — placeholder).</summary>
    public int ReservationTtlSeconds { get; set; } = 900;
}
