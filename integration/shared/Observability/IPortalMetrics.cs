namespace PartsPortal.Shared.Observability;

/// <summary>
/// Portal metrics (TDD §11, §12): oversell pressure, reservation-leak control, DLQ depth, and
/// sync activity. Emitted via System.Diagnostics.Metrics so any OpenTelemetry/App Insights
/// exporter can scrape them. Correlation ids are carried separately on every hop (TDD §11).
/// </summary>
public interface IPortalMetrics
{
    /// <summary>A reserve attempt fell short (oversell pressure / shortfall at the checkout gate).</summary>
    void ReserveShortfall();

    /// <summary>Stale soft reservations released by the TTL job (reservation-leak control).</summary>
    void ReservationsReleased(int count);

    /// <summary>An order writeback permanently failed and was dead-lettered (DLQ depth).</summary>
    void OrderDeadLettered();

    /// <summary>Catalog rows upserted by a sync run (sync activity / freshness).</summary>
    void CatalogSynced(int upserted);
}

/// <summary>No-op metrics (default / tests that don't assert on metrics).</summary>
public sealed class NoOpPortalMetrics : IPortalMetrics
{
    public static readonly NoOpPortalMetrics Instance = new();

    public void ReserveShortfall() { }

    public void ReservationsReleased(int count) { }

    public void OrderDeadLettered() { }

    public void CatalogSynced(int upserted) { }
}
