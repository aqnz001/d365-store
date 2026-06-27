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
    // Sweep cadence (DR-015): every 2 min — well below the 15-min TTL so worst-case over-hold is
    // ≈ TTL + sweep (~17 min). Override at deploy with the Ivs:ReservationSweepCron app setting
    // (use a "%Ivs:ReservationSweepCron%" binding expression). The TTL itself is config-driven
    // (Ivs:ReservationTtlSeconds — DR-015); releases go via the IVS interface only (Golden Rule #2).
    [Function("ReservationRelease")]
    public async Task Run([TimerTrigger("0 */2 * * * *")] TimerInfo timer, FunctionContext context)
    {
        var released = await release.ReleaseStaleAsync(DateTimeOffset.UtcNow, context.CancellationToken);
        context.GetLogger<ReservationReleaseFunction>()
            .LogInformation("Reservation-release tick: {Released} stale reservation(s) released.", released);
    }
}
