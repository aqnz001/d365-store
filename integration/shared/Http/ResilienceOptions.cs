namespace PartsPortal.Shared.Http;

/// <summary>
/// Config-driven resilience policy for outbound HTTP to the external systems
/// (TDD §9: exponential backoff + jitter, bounded retries, timeout). Bound from the
/// "Resilience" configuration section; never hardcoded literals.
/// </summary>
public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    /// <summary>Number of retries after the initial attempt (total attempts = 1 + this).</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the first retry; subsequent retries grow exponentially (with jitter).</summary>
    public int BaseDelayMilliseconds { get; set; } = 200;

    /// <summary>Per-attempt-pipeline total timeout.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
