namespace PartsPortal.Bff.Payments;

/// <summary>Payment authorization outcome.</summary>
public enum PaymentStatus
{
    Succeeded,
    Declined,
    RequiresAction,
}

/// <summary>A payment to authorize. The card itself is collected by the provider's hosted
/// fields (SAQ-A) — only an opaque token reaches the BFF, never the PAN (DR-003).</summary>
public sealed record PaymentRequest(decimal Amount, string Currency, string PaymentToken, string CustomerAccount);

/// <summary>Result of authorizing a payment.</summary>
public sealed record PaymentResult(PaymentStatus Status, string? PaymentReference, string? Message);

/// <summary>
/// Payment gateway abstraction (DR-003: Stripe, PCI scope SAQ-A). The implementation is chosen
/// by config (Payments:Provider). Card data is never handled by the BFF — the SPA uses the
/// provider's hosted Payment Element and the BFF authorizes against an opaque token.
/// </summary>
public interface IPaymentProvider
{
    Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken ct = default);
}
