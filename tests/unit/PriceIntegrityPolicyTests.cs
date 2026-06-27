using PartsPortal.Shared.Writeback;
using Xunit;

namespace PartsPortal.Tests.Unit;

/// <summary>
/// T11 (price integrity) / TDD §9 — the locked-price tolerance rule at writeback: drift within
/// the configured fraction of the locked price is honored; beyond it routes to CSR review.
/// Tolerance is config-supplied; assertions never depend on a hardcoded literal.
/// </summary>
public class PriceIntegrityPolicyTests
{
    private static PriceIntegrityPolicy Policy(decimal toleranceFraction) =>
        new(new PriceIntegrityOptions { ToleranceFraction = toleranceFraction });

    [Theory]
    [InlineData(10.00, 10.00)] // identical
    [InlineData(10.00, 10.40)] // +4% < 5%
    [InlineData(10.00, 9.60)]  // -4% < 5%
    [InlineData(10.00, 10.50)] // exactly +5% (boundary is inclusive)
    public void Within_tolerance_is_honored(double locked, double current)
        => Assert.Equal(PriceIntegrityDecision.Honor,
            Policy(0.05m).Evaluate((decimal)locked, (decimal)current));

    [Theory]
    [InlineData(10.00, 10.51)] // just over +5%
    [InlineData(10.00, 12.00)] // +20%
    [InlineData(10.00, 8.00)]  // -20%
    public void Beyond_tolerance_routes_to_csr_review(double locked, double current)
        => Assert.Equal(PriceIntegrityDecision.CsrReview,
            Policy(0.05m).Evaluate((decimal)locked, (decimal)current));

    [Fact]
    public void Zero_tolerance_blocks_any_drift()
    {
        var policy = Policy(0m);
        Assert.Equal(PriceIntegrityDecision.Honor, policy.Evaluate(10m, 10m));
        Assert.Equal(PriceIntegrityDecision.CsrReview, policy.Evaluate(10m, 10.01m));
    }

    [Fact]
    public void Zero_locked_price_falls_back_to_absolute_tolerance()
    {
        // No positive base to take a fraction of: tolerance is treated as an absolute amount.
        var policy = Policy(0.5m);
        Assert.Equal(PriceIntegrityDecision.Honor, policy.Evaluate(0m, 0.5m));
        Assert.Equal(PriceIntegrityDecision.CsrReview, policy.Evaluate(0m, 0.6m));
    }

    [Fact]
    public void Null_options_throws()
        => Assert.Throws<ArgumentNullException>(() => new PriceIntegrityPolicy(null!));
}
