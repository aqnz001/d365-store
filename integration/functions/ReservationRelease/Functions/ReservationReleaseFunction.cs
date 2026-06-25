using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace PartsPortal.Functions.ReservationRelease;

/// <summary>
/// Timer job that releases stale soft reservations past their TTL (TDD §7.1; reservation
/// state machine). TTL value + release triggers are an Open Decision (#4) — config-driven,
/// never hardcoded. Releases go through the IVS interface only (Golden Rule #2). Logic in T12.
/// </summary>
public class ReservationReleaseFunction
{
    // Placeholder cadence; TTL and cadence come from configuration (TODO(T12)).
    [Function("ReservationRelease")]
    public void Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer, FunctionContext context)
    {
        var log = context.GetLogger<ReservationReleaseFunction>();
        // TODO(T12): find soft reservations past TTL → release via IVS → emit reservation-leak metric.
        log.LogInformation("Scaffolded reservation-release tick.");
    }
}
