namespace PartsPortal.Shared.Saga;

/// <summary>
/// Executes a post-payment writeback saga (TDD §8) with compensation.
/// Steps run in order; on the first permanent failure the steps that have already
/// completed are compensated in reverse order (release reservation, route to CSR,
/// dead-letter, etc.). Compensation is best-effort: a failing compensation does not
/// stop the rest from running. All failures surface via <see cref="SagaException"/>.
/// </summary>
public static class SagaRunner
{
    /// <summary>
    /// Runs every step's <see cref="ISagaStep.ExecuteAsync"/> in order. If a step throws,
    /// the already-completed steps are compensated in reverse order (the throwing step did
    /// not complete and is NOT compensated) and a <see cref="SagaException"/> is thrown that
    /// wraps the original failure plus any compensation failures. Returns normally on success.
    /// </summary>
    public static async Task RunAsync(IReadOnlyList<ISagaStep> steps, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(steps);

        // Steps that ran ExecuteAsync to completion, recorded in execution order.
        var completed = new List<ISagaStep>(steps.Count);

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step is null)
            {
                throw new ArgumentException($"Saga step at index {i} is null.", nameof(steps));
            }

            try
            {
                await step.ExecuteAsync(ct).ConfigureAwait(false);
                completed.Add(step);
            }
            catch (Exception executeFailure)
            {
                // The throwing step did NOT complete, so it is not compensated.
                var compensationFailures = await CompensateAsync(completed, ct).ConfigureAwait(false);
                throw new SagaException(i, executeFailure, compensationFailures);
            }
        }
    }

    /// <summary>
    /// Best-effort compensation of completed steps in REVERSE order. Every step is given a
    /// chance to compensate even if an earlier compensation throws; collected failures are
    /// returned so the caller can surface them.
    /// </summary>
    private static async Task<IReadOnlyList<Exception>> CompensateAsync(
        IReadOnlyList<ISagaStep> completed,
        CancellationToken ct)
    {
        List<Exception>? failures = null;

        for (var i = completed.Count - 1; i >= 0; i--)
        {
            try
            {
                await completed[i].CompensateAsync(ct).ConfigureAwait(false);
            }
            catch (Exception compensateFailure)
            {
                (failures ??= new List<Exception>()).Add(compensateFailure);
            }
        }

        return failures is null
            ? Array.Empty<Exception>()
            : failures;
    }
}

/// <summary>
/// Thrown when a saga fails. Wraps the original execution failure (as
/// <see cref="Exception.InnerException"/>) and any failures encountered while compensating
/// the already-completed steps.
/// </summary>
public sealed class SagaException : Exception
{
    /// <summary>Zero-based index of the step whose <see cref="ISagaStep.ExecuteAsync"/> threw.</summary>
    public int FailedStepIndex { get; }

    /// <summary>Failures raised by <see cref="ISagaStep.CompensateAsync"/> during rollback (may be empty).</summary>
    public IReadOnlyList<Exception> CompensationFailures { get; }

    public SagaException(int failedStepIndex, Exception executeFailure, IReadOnlyList<Exception> compensationFailures)
        : base(BuildMessage(failedStepIndex, compensationFailures), executeFailure)
    {
        FailedStepIndex = failedStepIndex;
        CompensationFailures = compensationFailures ?? Array.Empty<Exception>();
    }

    private static string BuildMessage(int failedStepIndex, IReadOnlyList<Exception> compensationFailures)
    {
        var compensationCount = compensationFailures?.Count ?? 0;
        return compensationCount == 0
            ? $"Saga failed at step {failedStepIndex}; compensation of completed steps succeeded."
            : $"Saga failed at step {failedStepIndex}; {compensationCount} compensation step(s) also failed.";
    }
}
