namespace PartsPortal.Shared.Models;

/// <summary>
/// Carries the correlation ID propagated end-to-end: cart → reserve → order → fulfilment
/// (TDD §11, SAD §9). APIM injects it on ingress; it travels on Service Bus messages.
/// Must never carry personal data — it is an opaque trace identifier only.
/// </summary>
public sealed record CorrelationContext(string CorrelationId)
{
    public const string HeaderName = "x-correlation-id";

    /// <summary>Creates a context with a fresh, unique, non-empty correlation id.</summary>
    public static CorrelationContext New() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Uses <paramref name="candidate"/> when it is non-null and not whitespace;
    /// otherwise generates a fresh id via <see cref="New"/>.
    /// </summary>
    public static CorrelationContext FromValueOrNew(string? candidate) =>
        string.IsNullOrWhiteSpace(candidate) ? New() : new CorrelationContext(candidate);

    /// <summary>
    /// Reads <see cref="HeaderName"/> (case-insensitively) from incoming headers /
    /// Service Bus application properties, falling back to a fresh id when absent or blank.
    /// </summary>
    public static CorrelationContext FromHeaders(IReadOnlyDictionary<string, string?> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, HeaderName, StringComparison.OrdinalIgnoreCase))
            {
                return FromValueOrNew(value);
            }
        }

        return New();
    }
}
