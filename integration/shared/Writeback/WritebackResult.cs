namespace PartsPortal.Shared.Writeback;

/// <summary>Terminal outcome of processing an order writeback message.</summary>
public enum WritebackStatus
{
    /// <summary>Order created in FinOps (header + lines); reservation converts to physical.</summary>
    Created,

    /// <summary>Duplicate message — the existing sales order number is returned, nothing re-created.</summary>
    Duplicate,

    /// <summary>Permanent failure — reservations released, routed to CSR; message dead-lettered.</summary>
    PermanentFailure,
}

/// <summary>Result of a writeback attempt (TDD §6.2, §8).</summary>
public sealed record WritebackResult(WritebackStatus Status, string? SalesOrderNumber, string? Reason)
{
    public static WritebackResult Created(string salesOrderNumber) => new(WritebackStatus.Created, salesOrderNumber, null);

    public static WritebackResult Duplicate(string salesOrderNumber) => new(WritebackStatus.Duplicate, salesOrderNumber, null);

    public static WritebackResult Permanent(string reason) => new(WritebackStatus.PermanentFailure, null, reason);
}
