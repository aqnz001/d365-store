namespace PartsPortal.Shared.Ivs;

/// <summary>ATP/AFR for a single dimension (never raw on-hand — Golden Rule #4).</summary>
public readonly record struct AtpResult(decimal Afr, decimal Atp);

/// <summary>
/// Outcome of a soft reservation: either reserved (with an id) or a shortfall with the
/// quantity that could have been reserved.
/// </summary>
public readonly record struct IvsReserveResult(bool Reserved, string? ReservationId, decimal AvailableQuantity);

/// <summary>
/// The IVS interface — the sole authority for inventory availability and reservation
/// (Golden Rule #2). Phase 1 talks to ivs-sim; Phase 2 to the real IVS sandbox, behind
/// this same interface (Golden Rule #1).
/// </summary>
public interface IIvsClient
{
    Task<AtpResult> QueryAtpAsync(string productId, string site, string location, CancellationToken ct = default);

    Task<IvsReserveResult> ReserveAsync(string productId, string site, string location, decimal quantity, CancellationToken ct = default);

    Task ReleaseAsync(string reservationId, CancellationToken ct = default);
}
