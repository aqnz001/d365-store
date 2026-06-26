namespace PartsPortal.Shared.Writeback;

/// <summary>
/// A transient writeback failure (e.g. OData 503/throttling after retries). Propagates so the
/// Service Bus message is redelivered and retried; dead-letters on max delivery (TDD §9).
/// </summary>
public sealed class TransientWritebackException(string message) : Exception(message);

/// <summary>
/// A permanent writeback failure (e.g. validation / missing master data). Triggers
/// compensation (release reservations, route to CSR) and dead-letters now — no retry (TDD §8/§9).
/// </summary>
public sealed class PermanentWritebackException(string message) : Exception(message);
