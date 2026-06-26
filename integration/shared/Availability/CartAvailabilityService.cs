using Microsoft.Extensions.Options;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Ivs;
using PartsPortal.Shared.Mapping;
using PartsPortal.Shared.Observability;
using PartsPortal.Shared.Reservations;

namespace PartsPortal.Shared.Availability;

/// <summary>
/// Implements the checkout-gate availability flow (TDD §6.1, §7.2). Per line it does a live
/// IVS ATP read, derives the published band + decision (config-driven buffers/thresholds),
/// and — on reserve — places soft reservations. Reserve is ALL-OR-NOTHING (DR-009): if any
/// line falls short, partial reservations are released so we never hold stock for a cart
/// that cannot be committed, and the caller is given the per-line shortfall to offer options.
/// </summary>
public sealed class CartAvailabilityService(
    IIvsClient ivs,
    AvailabilityBandCalculator bandCalculator,
    IOptions<IvsOptions> ivsOptions,
    IReservationRegistry reservations,
    IPortalMetrics metrics) : ICartAvailabilityService
{
    private readonly IvsOptions _ivs = ivsOptions.Value;

    public async Task<CartValidateResponse> ValidateAsync(CartValidateRequest request, string correlationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = new CartValidateResponse { CorrelationId = correlationId };
        foreach (var line in request.Lines)
        {
            var atp = await ivs.QueryAtpAsync(line.ItemNumber, line.Site, _ivs.DefaultLocation, ct);

            // Per-item class / backorderable / made-to-order / discontinued come from the
            // catalog attributes (T5) once joined here; defaults keep T6 self-contained.
            var result = bandCalculator.Calculate((decimal)atp.Atp, itemClass: string.Empty,
                backorderable: false, madeToOrder: false, discontinued: false);

            response.Lines.Add(new CartValidateLineResult
            {
                ItemNumber = line.ItemNumber,
                Band = result.Band,
                Decision = Decide(result, (decimal)line.Quantity),
                AvailableQuantity = (double)result.PublishedAtp,
            });
        }

        return response;
    }

    public async Task<(bool Reserved, ReserveResponse Response)> ReserveAsync(ReserveRequest request, string correlationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lines = new List<ReserveLineResult>();
        var reservationIds = new List<string>();
        var allReserved = true;

        foreach (var line in request.Lines)
        {
            var result = await ivs.ReserveAsync(line.ItemNumber, line.Site, _ivs.DefaultLocation, (decimal)line.Quantity, ct);
            if (result.Reserved)
            {
                reservationIds.Add(result.ReservationId!);
                // Track the soft reservation so the TTL job can release it if abandoned (TDD §7.1).
                reservations.Record(result.ReservationId!, DateTimeOffset.UtcNow, correlationId);
                lines.Add(new ReserveLineResult
                {
                    ItemNumber = line.ItemNumber,
                    Reserved = true,
                    ReservedQuantity = line.Quantity,
                    Shortfall = 0,
                    ReservationId = result.ReservationId,
                });
            }
            else
            {
                allReserved = false;
                metrics.ReserveShortfall();
                lines.Add(new ReserveLineResult
                {
                    ItemNumber = line.ItemNumber,
                    Reserved = false,
                    ReservedQuantity = (double)result.AvailableQuantity,
                    Shortfall = line.Quantity - (double)result.AvailableQuantity,
                    ReservationId = string.Empty,
                });
            }
        }

        var response = new ReserveResponse { CorrelationId = correlationId, TtlSeconds = _ivs.ReservationTtlSeconds };
        foreach (var result in lines)
        {
            response.Lines.Add(result);
        }

        if (!allReserved)
        {
            // All-or-nothing: release any partial reservations (DR-009).
            foreach (var id in reservationIds)
            {
                await ivs.ReleaseAsync(id, ct);
                reservations.MarkReleased(id);
            }

            return (false, response);
        }

        foreach (var id in reservationIds)
        {
            response.ReservationIds.Add(id);
        }

        return (true, response);
    }

    public async Task ReleaseAsync(ReleaseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var id in request.ReservationIds)
        {
            await ivs.ReleaseAsync(id, ct);
            reservations.MarkReleased(id);
        }
    }

    private static LineDecision Decide(AvailabilityResult result, decimal quantity) => result.Band switch
    {
        AvailabilityBand.Unavailable => LineDecision.Block,
        AvailabilityBand.Backorder => LineDecision.AllowBackorder,
        AvailabilityBand.MadeToOrder => LineDecision.Allow,
        _ => result.PublishedAtp >= quantity ? LineDecision.Allow
            : result.PublishedAtp > 0m ? LineDecision.ReduceQuantity
            : LineDecision.Block,
    };
}
