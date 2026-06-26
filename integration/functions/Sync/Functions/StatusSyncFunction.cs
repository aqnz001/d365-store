using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PartsPortal.Shared.Contracts.Messages;
using PartsPortal.Shared.Status;

namespace PartsPortal.Functions.Sync;

/// <summary>
/// Consumes FinOps fulfilment status events from the status-outbound topic and mirrors them to
/// the storefront order status (TDD §6.3): one order → multiple fulfilments with tracking, with
/// remaining backorder reflected. Catalog read path stays BYOD-only (Golden Rule #3).
/// </summary>
public class StatusSyncFunction(IStatusSyncService statusSync)
{
    [Function("StatusSync")]
    public void Run(
        [ServiceBusTrigger("status-outbound", "storefront", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        FunctionContext context)
    {
        var statusEvent = message.Body.ToObjectFromJson<FulfilmentStatusEvent>();
        if (statusEvent is null)
        {
            context.GetLogger<StatusSyncFunction>().LogError("Could not deserialize status event {MessageId}.", message.MessageId);
            return;
        }

        statusSync.Apply(statusEvent);
    }
}
