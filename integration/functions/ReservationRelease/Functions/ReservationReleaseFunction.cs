using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PartsPortal.Shared.Reservations;

namespace PartsPortal.Functions.ReservationRelease;

/// <summary>
/// Timer job that releases stale soft reservations past their TTL (TDD §7.1; reservation
/// state machine). TTL is config-driven (IvsOptions, Open Decision #4); releases go through
/// the IVS interface only (Golden Rule #2).
/// </summary>
public class ReservationReleaseFunction(ReservationReleaseService release)
{
    // Cadence is a placeholder; TTL comes from configuration (Ivs:ReservationTtlSeconds).
    [Function("ReservationRelease")]
    public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer, FunctionContext context)
    {
        var released = await release.ReleaseStaleAsync(DateTimeOffset.UtcNow, context.CancellationToken);
        context.GetLogger<ReservationReleaseFunction>()
            .LogInformation("Reservation-release tick: {Released} stale reservation(s) released.", released);
    }
}
