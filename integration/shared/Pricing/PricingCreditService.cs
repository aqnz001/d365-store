namespace PartsPortal.Shared.Pricing;

/// <summary>
/// Resolves locked prices + the credit decision for a cart (TDD §4.5, §6.1, §9).
/// Prices are locked from the service; the credit status is mapped to a gate decision:
/// OK → Approved, over-limit → RequiresApproval (draft/approval), hold → Blocked.
/// </summary>
public interface IPricingCreditService
{
    Task<CartPricingResult> ResolveAsync(PricingResolveRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class PricingCreditService(IPricingCreditClient client) : IPricingCreditService
{
    public async Task<CartPricingResult> ResolveAsync(PricingResolveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await client.ResolveAsync(request.CustomerAccount, request.Lines, ct);
        return new CartPricingResult(
            result.CustomerAccount,
            result.CreditStatus,
            MapDecision(result.CreditStatus),
            result.Lines,
            result.AvailableCredit,
            result.CreditLimit);
    }

    /// <summary>Maps the service's credit status to a gate decision (unknown → Blocked, fail-safe).</summary>
    public static CreditDecision MapDecision(string creditStatus) => (creditStatus ?? string.Empty).ToLowerInvariant() switch
    {
        "ok" => CreditDecision.Approved,
        "over-limit" => CreditDecision.RequiresApproval,
        "hold" => CreditDecision.Blocked,
        _ => CreditDecision.Blocked,
    };
}
