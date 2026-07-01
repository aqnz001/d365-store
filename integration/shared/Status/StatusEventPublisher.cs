using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using PartsPortal.Shared.Contracts.Messages;

namespace PartsPortal.Shared.Status;

/// <summary>Config for the status-outbound topic the fulfilment events are emitted to (TDD §6.3).</summary>
public sealed class StatusOutboundOptions
{
    public const string SectionName = "StatusOutbound";

    /// <summary>Service Bus topic name fulfilment status events are published to.</summary>
    public string TopicName { get; set; } = "status-outbound";
}

/// <summary>
/// Emits FinOps fulfilment business events (pack/ship/invoice/return/cancel) onto the
/// status-outbound pipeline (TDD §6.3). In production FinOps is the source; this seam also lets a
/// sandbox/integration harness inject events. Two implementations: Service Bus (real topic) and
/// in-process (applies straight to the local order-status mirror for the dev host / tests).
/// </summary>
public interface IStatusEventPublisher
{
    Task PublishAsync(FulfilmentStatusEvent statusEvent, CancellationToken ct = default);
}

/// <summary>
/// Publishes events to the status-outbound Service Bus topic, sessioned by sales order number so a
/// single order's events stay ordered (matches the StatusSync trigger's session subscription).
/// Build-verified; exercised against a live namespace at deploy.
/// </summary>
public sealed class ServiceBusStatusEventPublisher(ServiceBusClient client, IOptions<StatusOutboundOptions> options) : IStatusEventPublisher
{
    private readonly ServiceBusSender _sender = client.CreateSender(options.Value.TopicName);

    public async Task PublishAsync(FulfilmentStatusEvent statusEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(statusEvent);

        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(statusEvent))
        {
            ContentType = "application/json",
            Subject = statusEvent.EventType.ToString(),
            SessionId = statusEvent.SalesOrderNumber, // per-order ordering
            CorrelationId = statusEvent.CorrelationId,
        };

        await _sender.SendMessageAsync(message, ct);
    }
}

/// <summary>
/// In-process publisher: applies the event straight to the local <see cref="IStatusSyncService"/>
/// (no Service Bus). Used by the dev host and tests so the status pipeline is exercisable without
/// a broker — the same code path the Service Bus trigger drives in production.
/// </summary>
public sealed class InProcessStatusEventPublisher(IStatusSyncService statusSync) : IStatusEventPublisher
{
    public Task PublishAsync(FulfilmentStatusEvent statusEvent, CancellationToken ct = default) =>
        statusSync.ApplyAsync(statusEvent, ct);
}
