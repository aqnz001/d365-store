using System.Text.Json.Serialization;

namespace PartsPortal.Shared.Pricing;

/// <summary>A cart line to price (TDD §4.5).</summary>
public sealed record PricingResolveLine(string ItemNumber, decimal Quantity);

/// <summary>Request to resolve effective price + credit for a customer's cart.</summary>
public sealed record PricingResolveRequest(string CustomerAccount, IReadOnlyList<PricingResolveLine> Lines);

/// <summary>A priced line: net effective price (trade agreements applied) per the pricing service,
/// plus the FinOps-owned tax for the line (the portal surfaces tax, it never computes it — DR-021).
/// <see cref="GrossEffectivePrice"/> is net + tax — what the customer actually pays.</summary>
public sealed record PricedLine(
    string ItemNumber,
    decimal Quantity,
    decimal UnitPrice,
    decimal NetEffectivePrice,
    decimal TaxRate = 0m,
    decimal TaxAmount = 0m)
{
    public decimal GrossEffectivePrice => NetEffectivePrice + TaxAmount;
}

/// <summary>Raw pricing-service result (credit status as returned by the service).</summary>
public sealed record PricingResult(string CustomerAccount, string CreditStatus, IReadOnlyList<PricedLine> Lines);

/// <summary>Credit gate outcome at the checkout gate (TDD §9 credit row).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CreditDecision>))]
public enum CreditDecision
{
    /// <summary>Credit OK — proceed.</summary>
    Approved,

    /// <summary>Over limit — route to approval / draft order (do not auto-commit).</summary>
    RequiresApproval,

    /// <summary>On hold — block.</summary>
    Blocked,
}

/// <summary>
/// Enriched checkout-gate pricing result: locked prices per line + the credit decision the
/// storefront acts on (lock prices onto the cart; block/approval on hold/over-limit).
/// </summary>
public sealed record CartPricingResult(
    string CustomerAccount,
    string CreditStatus,
    CreditDecision Decision,
    IReadOnlyList<PricedLine> Lines);
