using System.Collections.Concurrent;

namespace PartsPortal.Shared.Reservations;

/// <summary>
/// In-memory reservation registry for Phase 1 (per-process). Phase 2 swaps a durable shared
/// store behind <see cref="IReservationRegistry"/> with no caller change.
/// </summary>
public sealed class InMemoryReservationRegistry : IReservationRegistry
{
    private readonly ConcurrentDictionary<string, ReservationEntry> _entries = new(StringComparer.Ordinal);

    public void Record(string reservationId, DateTimeOffset placedAtUtc, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationId);
        _entries[reservationId] = new ReservationEntry(reservationId, placedAtUtc, correlationId, ReservationState.Soft);
    }

    public void MarkConverted(string reservationId) => Transition(reservationId, ReservationState.Converted);

    public void MarkReleased(string reservationId) => Transition(reservationId, ReservationState.Released);

    public IReadOnlyList<ReservationEntry> FindStaleSoft(DateTimeOffset cutoffUtc) =>
        _entries.Values
            .Where(e => e.State == ReservationState.Soft && e.PlacedAtUtc < cutoffUtc)
            .ToList();

    private void Transition(string reservationId, ReservationState state)
    {
        if (string.IsNullOrWhiteSpace(reservationId))
        {
            return;
        }

        _entries.AddOrUpdate(
            reservationId,
            _ => new ReservationEntry(reservationId, default, string.Empty, state),
            (_, existing) => existing with { State = state });
    }
}
