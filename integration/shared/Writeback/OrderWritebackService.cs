using Microsoft.Extensions.Logging;
using PartsPortal.Shared.Contracts.Messages;
using PartsPortal.Shared.Idempotency;
using PartsPortal.Shared.Ivs;
using PartsPortal.Shared.Reservations;

namespace PartsPortal.Shared.Writeback;

/// <summary>
/// Processes a queue-backed order writeback message (TDD §6.2, §8; Golden Rules #6/#7/#8).
///
/// Flow: idempotency de-dup → create header → create lines (FK) via OData. The reservation
/// ids travel on the order, so FinOps/IVS convert soft → physical on order create (no explicit
/// middleware call on success — TDD §6.2). On PERMANENT failure the saga compensates: release
/// the soft reservations so stock isn't held, and route to CSR (the caller dead-letters).
/// TRANSIENT failures propagate so Service Bus retries with backoff.
/// </summary>
public sealed class OrderWritebackService(
    IIdempotencyStore idempotency,
    IODataOrderClient odata,
    IIvsClient ivs,
    IReservationRegistry reservations,
    ILogger<OrderWritebackService> logger)
{
    public async Task<WritebackResult> ProcessAsync(OrderInboundMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // De-dup before any FinOps create (Golden Rule #6) — a duplicate returns the original
        // sales order number and never creates a second order.
        var existing = await idempotency.TryGetAsync(message.IdempotencyKey, ct);
        if (existing is not null)
        {
            return WritebackResult.Duplicate(existing);
        }

        string salesOrderNumber;
        try
        {
            salesOrderNumber = await odata.CreateHeaderAsync(message.CustomerAccount, ct);
            foreach (var line in message.Lines)
            {
                await odata.CreateLineAsync(salesOrderNumber, line.ItemNumber, (decimal)line.Quantity, ct);
            }
        }
        catch (PermanentWritebackException ex)
        {
            await CompensateAsync(message, ct);
            return WritebackResult.Permanent(ex.Message);
        }
        // TransientWritebackException intentionally propagates → Service Bus redelivers (retry/DLQ).

        // The order carries the reservation ids; FinOps/IVS convert soft → physical on create.
        // Mark them converted so the TTL release job won't release a committed reservation.
        foreach (var line in message.Lines)
        {
            reservations.MarkConverted(line.ReservationReference);
        }

        await idempotency.SetAsync(message.IdempotencyKey, salesOrderNumber, ct);
        logger.LogInformation("Order {SalesOrderNumber} written back (idempotency {Key}).",
            salesOrderNumber, message.IdempotencyKey);
        return WritebackResult.Created(salesOrderNumber);
    }

    private async Task CompensateAsync(OrderInboundMessage message, CancellationToken ct)
    {
        // Saga compensation (TDD §8): release the soft reservations so stock isn't held; CSR follow-up.
        foreach (var line in message.Lines)
        {
            if (!string.IsNullOrWhiteSpace(line.ReservationReference))
            {
                await ivs.ReleaseAsync(line.ReservationReference, ct);
                reservations.MarkReleased(line.ReservationReference);
            }
        }

        logger.LogWarning("Writeback permanent failure for {Key}; reservations released, routed to CSR.",
            message.IdempotencyKey);
    }
}
