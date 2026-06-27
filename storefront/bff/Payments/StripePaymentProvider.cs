using Stripe;

namespace PartsPortal.Bff.Payments;

/// <summary>
/// Stripe payment provider (DR-003, owner-confirmed; PCI scope SAQ-A). Selected when
/// Payments:Provider == "Stripe". The card is collected by Stripe's hosted Payment Element in the
/// SPA, which yields a PaymentMethod id (pm_…) — the only token that reaches the BFF. Here the
/// BFF creates and confirms a PaymentIntent for the locked amount server-side.
///
/// The secret key comes from Key Vault via managed identity (Golden Rule #9) — never committed.
/// Live verification needs real Stripe keys, so this is build-verified here and exercised in a
/// Stripe test environment at deploy.
/// </summary>
public sealed class StripePaymentProvider(IConfiguration configuration) : IPaymentProvider
{
    private readonly StripeClient _client = new(configuration["Payments:Stripe:SecretKey"] ?? string.Empty);

    public async Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = new PaymentIntentCreateOptions
        {
            Amount = (long)Math.Round(request.Amount * 100m), // minor units
            Currency = request.Currency.ToLowerInvariant(),
            PaymentMethod = request.PaymentToken,
            Confirm = true,
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
                AllowRedirects = "never",
            },
            Metadata = new Dictionary<string, string> { ["customerAccount"] = request.CustomerAccount },
        };

        var intent = await new PaymentIntentService(_client).CreateAsync(options, cancellationToken: ct);

        return intent.Status switch
        {
            "succeeded" => new PaymentResult(PaymentStatus.Succeeded, intent.Id, null),
            "requires_action" => new PaymentResult(PaymentStatus.RequiresAction, intent.Id, "Additional authentication required."),
            _ => new PaymentResult(PaymentStatus.Declined, intent.Id, intent.Status),
        };
    }
}
