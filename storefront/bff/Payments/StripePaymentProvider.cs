namespace PartsPortal.Bff.Payments;

/// <summary>
/// Stripe payment provider (DR-003, owner-confirmed). Selected when Payments:Provider == "Stripe".
///
/// SAQ-A flow (no card data touches the BFF):
///   1. SPA renders the Stripe Payment Element (publishable key) and collects the card.
///   2. BFF creates a PaymentIntent for the locked amount/currency (Stripe secret key from Key
///      Vault — Golden Rule #9) and returns its client_secret to the SPA.
///   3. SPA confirms with Stripe; the BFF confirms/verifies the PaymentIntent status here.
///
/// TODO(S5-deploy): add the Stripe.net package + wire PaymentIntents using StripeOptions
/// (publishable key, secret key via Key Vault, webhook secret). Until then this provider is not
/// selected; the FakePaymentProvider serves Phase 1. Build proceeds against IPaymentProvider so
/// the swap is config-only.
/// </summary>
public sealed class StripePaymentProvider : IPaymentProvider
{
    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "Stripe provider not yet wired (TODO S5-deploy: add Stripe.net + Key Vault secret). " +
            "Set Payments:Provider=Fake for Phase 1.");
}
