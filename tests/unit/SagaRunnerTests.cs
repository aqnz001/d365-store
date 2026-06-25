using PartsPortal.Shared.Saga;
using Xunit;

namespace PartsPortal.Tests.Unit;

public sealed class SagaRunnerTests
{
    /// <summary>
    /// Recording fake step. Appends "exec:{name}" / "comp:{name}" to a shared log so tests can
    /// assert ordering, and can be configured to throw on execute or compensate.
    /// </summary>
    private sealed class RecordingStep : ISagaStep
    {
        private readonly List<string> _log;
        private readonly string _name;
        private readonly bool _failExecute;
        private readonly bool _failCompensate;

        public RecordingStep(List<string> log, string name, bool failExecute = false, bool failCompensate = false)
        {
            _log = log;
            _name = name;
            _failExecute = failExecute;
            _failCompensate = failCompensate;
        }

        public Task ExecuteAsync(CancellationToken ct = default)
        {
            _log.Add($"exec:{_name}");
            if (_failExecute)
            {
                throw new InvalidOperationException($"execute failed: {_name}");
            }

            return Task.CompletedTask;
        }

        public Task CompensateAsync(CancellationToken ct = default)
        {
            _log.Add($"comp:{_name}");
            if (_failCompensate)
            {
                throw new InvalidOperationException($"compensate failed: {_name}");
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task RunAsync_AllSucceed_ExecutesEveryStepInOrder_AndCompensatesNone()
    {
        var log = new List<string>();
        var steps = new ISagaStep[]
        {
            new RecordingStep(log, "A"),
            new RecordingStep(log, "B"),
            new RecordingStep(log, "C"),
        };

        await SagaRunner.RunAsync(steps);

        Assert.Equal(new[] { "exec:A", "exec:B", "exec:C" }, log);
        Assert.DoesNotContain(log, entry => entry.StartsWith("comp:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_EmptySteps_CompletesWithoutError()
    {
        await SagaRunner.RunAsync(Array.Empty<ISagaStep>());
    }

    [Fact]
    public async Task RunAsync_FailureAtStepK_CompensatesCompletedStepsInReverse_AndNotTheThrowingStep()
    {
        var log = new List<string>();
        // Step at index 2 ("C") throws during execute. Completed steps are A (0) and B (1).
        var steps = new ISagaStep[]
        {
            new RecordingStep(log, "A"),
            new RecordingStep(log, "B"),
            new RecordingStep(log, "C", failExecute: true),
            new RecordingStep(log, "D"),
        };

        var ex = await Assert.ThrowsAsync<SagaException>(() => SagaRunner.RunAsync(steps));

        // Executes A, B, then C (which throws); D is never reached.
        // Compensation runs in reverse over completed steps only: B then A. C is NOT compensated.
        Assert.Equal(
            new[] { "exec:A", "exec:B", "exec:C", "comp:B", "comp:A" },
            log);
        Assert.DoesNotContain("comp:C", log);
        Assert.DoesNotContain("exec:D", log);

        Assert.Equal(2, ex.FailedStepIndex);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal("execute failed: C", ex.InnerException!.Message);
        Assert.Empty(ex.CompensationFailures);
    }

    [Fact]
    public async Task RunAsync_FailureAtFirstStep_CompensatesNothing()
    {
        var log = new List<string>();
        var steps = new ISagaStep[]
        {
            new RecordingStep(log, "A", failExecute: true),
            new RecordingStep(log, "B"),
        };

        var ex = await Assert.ThrowsAsync<SagaException>(() => SagaRunner.RunAsync(steps));

        Assert.Equal(new[] { "exec:A" }, log);
        Assert.Equal(0, ex.FailedStepIndex);
        Assert.Empty(ex.CompensationFailures);
    }

    [Fact]
    public async Task RunAsync_CompensationThrows_RemainingCompensationsStillRun_AndSurfaceViaSagaException()
    {
        var log = new List<string>();
        // A, B, C complete; D throws on execute. Compensation order is C, B, A.
        // B's compensation throws, but A and C must still compensate.
        var steps = new ISagaStep[]
        {
            new RecordingStep(log, "A"),
            new RecordingStep(log, "B", failCompensate: true),
            new RecordingStep(log, "C"),
            new RecordingStep(log, "D", failExecute: true),
        };

        var ex = await Assert.ThrowsAsync<SagaException>(() => SagaRunner.RunAsync(steps));

        Assert.Equal(
            new[] { "exec:A", "exec:B", "exec:C", "exec:D", "comp:C", "comp:B", "comp:A" },
            log);

        Assert.Equal(3, ex.FailedStepIndex);
        Assert.Equal("execute failed: D", ex.InnerException!.Message);

        // The single compensation failure (B) is surfaced; C and A compensated cleanly.
        var compFailure = Assert.Single(ex.CompensationFailures);
        Assert.IsType<InvalidOperationException>(compFailure);
        Assert.Equal("compensate failed: B", compFailure.Message);
    }

    [Fact]
    public async Task RunAsync_MultipleCompensationsThrow_AllSurfaceViaSagaException()
    {
        var log = new List<string>();
        // A and B complete; C throws on execute. Both A and B fail to compensate.
        var steps = new ISagaStep[]
        {
            new RecordingStep(log, "A", failCompensate: true),
            new RecordingStep(log, "B", failCompensate: true),
            new RecordingStep(log, "C", failExecute: true),
        };

        var ex = await Assert.ThrowsAsync<SagaException>(() => SagaRunner.RunAsync(steps));

        Assert.Equal(
            new[] { "exec:A", "exec:B", "exec:C", "comp:B", "comp:A" },
            log);

        Assert.Equal(2, ex.FailedStepIndex);
        Assert.Equal(2, ex.CompensationFailures.Count);
        // Reverse order: B compensated first, then A.
        Assert.Equal("compensate failed: B", ex.CompensationFailures[0].Message);
        Assert.Equal("compensate failed: A", ex.CompensationFailures[1].Message);
    }

    [Fact]
    public async Task RunAsync_NullSteps_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => SagaRunner.RunAsync(null!));
    }
}
