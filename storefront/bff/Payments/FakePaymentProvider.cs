namespace PartsPortal.Bff.Payments;

/// <summary>
/// Phase-1 payment provider used when Payments:Provider is "Fake" (default for local/test, since
/// real Stripe needs keys + network). Deterministic: a token of "decline" declines, "action"
/// requires action, anything else succeeds. Swapped for Stripe in Phase 2 (DR-003).
/// </summary>
public sealed class FakePaymentProvider : IPaymentProvider
{
    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = request.PaymentToken?.ToLowerInvariant() switch
        {
            "decline" => new PaymentResult(PaymentStatus.Declined, null, "Card declined (simulated)."),
            "action" => new PaymentResult(PaymentStatus.RequiresAction, null, "Additional authentication required (simulated)."),
            _ => new PaymentResult(PaymentStatus.Succeeded, $"PAY-{request.CustomerAccount}-{request.Amount}", null),
        };

        return Task.FromResult(result);
    }
}
