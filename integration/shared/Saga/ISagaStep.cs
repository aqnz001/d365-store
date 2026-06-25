namespace PartsPortal.Shared.Saga;

/// <summary>
/// One step in the post-payment writeback saga (TDD §8). On permanent failure the
/// orchestrator runs <see cref="CompensateAsync"/> in reverse to release reservations,
/// route to CSR, and dead-letter. Implementation lands in T4.
/// </summary>
public interface ISagaStep
{
    Task ExecuteAsync(CancellationToken ct = default);

    Task CompensateAsync(CancellationToken ct = default);
}
