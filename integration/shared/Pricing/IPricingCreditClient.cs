namespace PartsPortal.Shared.Pricing;

/// <summary>
/// The pricing/credit service interface (TDD §4.5). Phase 1 talks to pricing-credit-sim;
/// Phase 2 to the FinOps PortalPricing custom service, behind this same interface.
/// </summary>
public interface IPricingCreditClient
{
    Task<PricingResult> ResolveAsync(string customerAccount, IReadOnlyList<PricingResolveLine> lines, CancellationToken ct = default);
}
