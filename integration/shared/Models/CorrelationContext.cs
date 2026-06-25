namespace PartsPortal.Shared.Models;

/// <summary>
/// Carries the correlation ID propagated end-to-end: cart → reserve → order → fulfilment
/// (TDD §11, SAD §9). APIM injects it on ingress; it travels on Service Bus messages.
/// </summary>
public sealed record CorrelationContext(string CorrelationId)
{
    public const string HeaderName = "x-correlation-id";

    // TODO(T4): factory from incoming request / Service Bus application properties.
}
