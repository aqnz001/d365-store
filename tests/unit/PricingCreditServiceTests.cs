using PartsPortal.Shared.Pricing;
using Xunit;

namespace PartsPortal.Tests.Unit;

/// <summary>T7 — the credit-status → gate-decision mapping (TDD §9). Unknown statuses fail safe to Blocked.</summary>
public class PricingCreditServiceTests
{
    [Theory]
    [InlineData("OK", CreditDecision.Approved)]
    [InlineData("ok", CreditDecision.Approved)]
    [InlineData("over-limit", CreditDecision.RequiresApproval)]
    [InlineData("hold", CreditDecision.Blocked)]
    [InlineData("", CreditDecision.Blocked)]
    [InlineData("something-unexpected", CreditDecision.Blocked)]
    public void MapDecision_maps_credit_status_to_gate_decision(string status, CreditDecision expected)
        => Assert.Equal(expected, PricingCreditService.MapDecision(status));

    [Fact]
    public void GrossEffectivePrice_is_net_plus_finops_tax()
    {
        var line = new PricedLine("PART-1", 3m, 10m, 30m, TaxRate: 0.20m, TaxAmount: 6.00m);
        Assert.Equal(36.00m, line.GrossEffectivePrice);

        // No tax returned → gross == net (the portal never invents tax).
        Assert.Equal(30m, new PricedLine("PART-2", 3m, 10m, 30m).GrossEffectivePrice);
    }
}
