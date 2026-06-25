using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace PartsPortal.Functions.Writeback;

/// <summary>
/// Queue-triggered order writeback (TDD §6.2, §8; Golden Rules #7, #8). Sessions enforce
/// per-customer controlled concurrency so we never parallel-hammer the order entities.
/// Idempotency de-dup, header→lines create, reservation conversion, DLQ + saga land in T9.
/// </summary>
public class OrderWritebackFunction
{
    [Function("OrderWriteback")]
    public void Run(
        [ServiceBusTrigger("orders-inbound", Connection = "ServiceBusConnection", IsSessionsEnabled = true)]
        ServiceBusReceivedMessage message,
        FunctionContext context)
    {
        var log = context.GetLogger<OrderWritebackFunction>();
        // TODO(T9): idempotency check → SalesOrderHeadersV2 → SalesOrderLines (FK) → convert reservation → DLQ/saga on failure.
        log.LogInformation("Scaffolded writeback received message {MessageId} (session {SessionId}).",
            message.MessageId, message.SessionId);
    }
}
