using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartsPortal.Shared.Ivs;
using PartsPortal.Shared.Observability;

namespace PartsPortal.Shared.Reservations;

/// <summary>
/// Releases stale soft reservations past their TTL (TDD §7.1 Soft→Released on expiry; §12
/// reservation-leak control). TTL is config-driven (IvsOptions, Open Decision #4). Releases go
/// through the IVS interface only (Golden Rule #2).
/// </summary>
public sealed class ReservationReleaseService(
    IReservationRegistry registry,
    IIvsClient ivs,
    IOptions<IvsOptions> options,
    IPortalMetrics metrics,
    ILogger<ReservationReleaseService> logger)
{
    /// <summary>Releases soft reservations older than the TTL relative to <paramref name="nowUtc"/>; returns the count released.</summary>
    public async Task<int> ReleaseStaleAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var ttlSeconds = options.Value.ReservationTtlSeconds;
        var cutoff = nowUtc - TimeSpan.FromSeconds(ttlSeconds);
        var stale = registry.FindStaleSoft(cutoff);

        foreach (var entry in stale)
        {
            await ivs.ReleaseAsync(entry.ReservationId, ct);
            registry.MarkReleased(entry.ReservationId);
        }

        if (stale.Count > 0)
        {
            metrics.ReservationsReleased(stale.Count);
            logger.LogInformation("Released {Count} stale soft reservation(s) past TTL {TtlSeconds}s.", stale.Count, ttlSeconds);
        }

        return stale.Count;
    }
}
