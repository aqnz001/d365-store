using System.Diagnostics.Metrics;

namespace PartsPortal.Shared.Observability;

/// <summary>
/// System.Diagnostics.Metrics implementation. Counters on the "PartsPortal" meter; exported by
/// whatever OpenTelemetry / Application Insights pipeline the host configures.
/// </summary>
public sealed class DiagnosticsPortalMetrics : IPortalMetrics, IDisposable
{
    public const string MeterName = "PartsPortal";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _reserveShortfalls;
    private readonly Counter<long> _reservationsReleased;
    private readonly Counter<long> _ordersDeadLettered;
    private readonly Counter<long> _catalogUpserts;

    public DiagnosticsPortalMetrics()
    {
        _reserveShortfalls = _meter.CreateCounter<long>("partsportal.reserve.shortfalls", description: "Reserve attempts that fell short (oversell pressure).");
        _reservationsReleased = _meter.CreateCounter<long>("partsportal.reservations.released_stale", description: "Stale soft reservations released by the TTL job.");
        _ordersDeadLettered = _meter.CreateCounter<long>("partsportal.writeback.dead_lettered", description: "Order writebacks dead-lettered after permanent failure.");
        _catalogUpserts = _meter.CreateCounter<long>("partsportal.catalog.upserts", description: "Catalog rows upserted by a sync run.");
    }

    public void ReserveShortfall() => _reserveShortfalls.Add(1);

    public void ReservationsReleased(int count)
    {
        if (count > 0)
        {
            _reservationsReleased.Add(count);
        }
    }

    public void OrderDeadLettered() => _ordersDeadLettered.Add(1);

    public void CatalogSynced(int upserted)
    {
        if (upserted > 0)
        {
            _catalogUpserts.Add(upserted);
        }
    }

    public void Dispose() => _meter.Dispose();
}
