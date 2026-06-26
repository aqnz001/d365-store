using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PartsPortal.Shared.Contracts.Messages;
using PartsPortal.Shared.Writeback;

namespace PartsPortal.Functions.Writeback;

/// <summary>
/// Queue-triggered order writeback (TDD §6.2, §8; Golden Rules #6/#7/#8). Sessions enforce
/// per-customer controlled concurrency so we never parallel-hammer the order entities. Thin
/// trigger delegating to <see cref="OrderWritebackService"/>: permanent failures dead-letter
/// (after compensation); transient failures propagate so Service Bus retries with backoff.
/// </summary>
public class OrderWritebackFunction(OrderWritebackService writeback)
{
    [Function("OrderWriteback")]
    public async Task Run(
        [ServiceBusTrigger("orders-inbound", Connection = "ServiceBusConnection", IsSessionsEnabled = true)]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        FunctionContext context)
    {
        var log = context.GetLogger<OrderWritebackFunction>();
        var order = message.Body.ToObjectFromJson<OrderInboundMessage>();
        if (order is null)
        {
            await messageActions.DeadLetterMessageAsync(message);
            log.LogError("Dead-lettered message {MessageId}: could not deserialize order.", message.MessageId);
            return;
        }

        // Transient failures throw out of here → message abandoned and redelivered (DLQ on max delivery).
        var result = await writeback.ProcessAsync(order, context.CancellationToken);

        if (result.Status == WritebackStatus.PermanentFailure)
        {
            await messageActions.DeadLetterMessageAsync(message);
            log.LogWarning("Dead-lettered message {MessageId} (session {SessionId}): {Reason}.",
                message.MessageId, message.SessionId, result.Reason);
            return;
        }

        await messageActions.CompleteMessageAsync(message);
        log.LogInformation("Writeback {Status} for {MessageId}: order {SalesOrderNumber}.",
            result.Status, message.MessageId, result.SalesOrderNumber);
    }
}
