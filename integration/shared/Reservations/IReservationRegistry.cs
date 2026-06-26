namespace PartsPortal.Shared.Reservations;

/// <summary>Soft-reservation lifecycle state (TDD §7.1).</summary>
public enum ReservationState
{
    Soft,
    Converted,
    Released,
}

/// <summary>A tracked soft reservation.</summary>
public sealed record ReservationEntry(string ReservationId, DateTimeOffset PlacedAtUtc, string CorrelationId, ReservationState State);

/// <summary>
/// Tracks soft reservations so the TTL release job can free stale ones (abandoned carts) and
/// avoid releasing those already converted (TDD §7.1, §12 reservation-leak). Phase 1 is
/// in-memory (per process); Phase 2 swaps a durable shared store (Redis/Table) behind this
/// interface so the release job — a separate Function app — sees reservations placed elsewhere.
/// </summary>
public interface IReservationRegistry
{
    void Record(string reservationId, DateTimeOffset placedAtUtc, string correlationId);

    void MarkConverted(string reservationId);

    void MarkReleased(string reservationId);

    /// <summary>Soft reservations placed before <paramref name="cutoffUtc"/> (i.e. past TTL).</summary>
    IReadOnlyList<ReservationEntry> FindStaleSoft(DateTimeOffset cutoffUtc);
}
