using PartsPortal.Shared.Contracts.Middleware;

namespace PartsPortal.Shared.Availability;

/// <summary>
/// Checkout-gate availability orchestration (TDD §6.1). Validate is the authoritative
/// live ATP read (bands, never counts); reserve places soft reservations via IVS (the
/// sole reservation authority); release frees them. Price/credit locking is added in T7.
/// </summary>
public interface ICartAvailabilityService
{
    Task<CartValidateResponse> ValidateAsync(CartValidateRequest request, string correlationId, CancellationToken ct = default);

    /// <summary>Returns whether the whole cart was reserved, plus the per-line response.</summary>
    Task<(bool Reserved, ReserveResponse Response)> ReserveAsync(ReserveRequest request, string correlationId, CancellationToken ct = default);

    Task ReleaseAsync(ReleaseRequest request, CancellationToken ct = default);
}
