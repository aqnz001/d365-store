namespace PartsPortal.Shared.Writeback;

/// <summary>
/// Config-bound tolerance for the writeback price-integrity check (TDD §9; Open Decision —
/// values come from configuration, never hardcoded literals).
/// </summary>
public sealed class PriceIntegrityOptions
{
    /// <summary>
    /// Maximum allowed drift of the current FinOps price away from the price locked at order
    /// time, as a fraction of the locked price (e.g. 0.05 = 5%). Within this the locked price is
    /// honored; beyond it the line is routed to CSR review rather than written back silently.
    /// </summary>
    public decimal ToleranceFraction { get; set; }
}

/// <summary>Outcome of comparing a locked line price against the current FinOps price.</summary>
public enum PriceIntegrityDecision
{
    /// <summary>Within tolerance — honor the locked price and write the order back.</summary>
    Honor,

    /// <summary>Beyond tolerance — do not auto-create; route to CSR review.</summary>
    CsrReview,
}

/// <summary>
/// Decides whether a line's locked price may still be honored at writeback given the current
/// FinOps price (TDD §9: "Honor locked price within tolerance; above → CSR review"). Pure and
/// config-driven; the drift is measured relative to the locked price.
/// </summary>
public sealed class PriceIntegrityPolicy(PriceIntegrityOptions options)
{
    private readonly PriceIntegrityOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Compares the <paramref name="lockedPrice"/> the customer agreed to against the
    /// <paramref name="currentPrice"/> now in FinOps. Returns <see cref="PriceIntegrityDecision.Honor"/>
    /// when the relative drift is within the configured tolerance, otherwise
    /// <see cref="PriceIntegrityDecision.CsrReview"/>.
    /// </summary>
    public PriceIntegrityDecision Evaluate(decimal lockedPrice, decimal currentPrice)
    {
        var drift = Math.Abs(currentPrice - lockedPrice);

        // Relative to the locked price when it is positive; fall back to absolute when the locked
        // price is zero/negative (no meaningful base to take a fraction of).
        var allowed = lockedPrice > 0m
            ? lockedPrice * _options.ToleranceFraction
            : _options.ToleranceFraction;

        return drift <= allowed ? PriceIntegrityDecision.Honor : PriceIntegrityDecision.CsrReview;
    }
}
