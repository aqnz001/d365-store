using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PartsPortal.Shared.Contracts.Middleware;

namespace PartsPortal.Shared.Writeback;

/// <summary>Config for the order-intake queue the checkout enqueues onto (TDD §5.5/§6.2).</summary>
public sealed class OrderIntakeOptions
{
    public const string SectionName = "OrderIntake";

    /// <summary>Service Bus queue (sessions enabled) the writeback trigger consumes.</summary>
    public string QueueName { get; set; } = "orders-inbound";
}

/// <summary>
/// Accepts a placed order and gets it onto the writeback path (Golden Rule #7: checkout never
/// blocks on the ERP). Two implementations: Service Bus (production — enqueue to orders-inbound,
/// sessioned per customer; the SO number is assigned later by the writeback consumer) and
/// in-process (dev host / tests — runs the writeback inline and returns the SO number immediately).
/// </summary>
public interface IOrderIntake
{
    Task<OrderStatusResponse> SubmitAsync(OrderRequest request, CancellationToken ct = default);
}

/// <summary>Enqueues the order to Service Bus; returns a Queued ack with the portal order id (DR-014).</summary>
public sealed class ServiceBusOrderIntake(ServiceBusClient client, IOptions<OrderIntakeOptions> options) : IOrderIntake
{
    private readonly ServiceBusSender _sender = client.CreateSender(options.Value.QueueName);

    public async Task<OrderStatusResponse> SubmitAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var message = OrderInboundMessageMapper.ToInboundMessage(request, DateTimeOffset.UtcNow);
        var sbMessage = new ServiceBusMessage(BinaryData.FromObjectAsJson(message))
        {
            SessionId = message.SessionId,
            MessageId = message.IdempotencyKey, // de-dup hint at the broker too
            ContentType = "application/json",
            CorrelationId = message.CorrelationId,
        };

        await _sender.SendMessageAsync(sbMessage, ct);

        // Async writeback (Golden Rule #7): no SO number yet — the portal order id is the customer's
        // reference (DR-014); the SO number is back-filled when writeback completes.
        return new OrderStatusResponse
        {
            OrderId = request.IdempotencyKey,
            Status = OrderStatus.Queued,
            CorrelationId = request.CorrelationId,
            Message = "Queued for writeback.",
        };
    }
}

/// <summary>Runs the writeback inline (dev host / tests) — returns the SO number synchronously.</summary>
public sealed class InProcessOrderIntake(OrderWritebackService writeback) : IOrderIntake
{
    public async Task<OrderStatusResponse> SubmitAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var message = OrderInboundMessageMapper.ToInboundMessage(request, DateTimeOffset.UtcNow);
        var result = await writeback.ProcessAsync(message, ct);
        return new OrderStatusResponse
        {
            OrderId = request.IdempotencyKey,
            SalesOrderNumber = result.SalesOrderNumber,
            Status = OrderStatus.WrittenBack,
            CorrelationId = request.CorrelationId,
            Message = result.Status.ToString(),
        };
    }
}

/// <summary>Picks the order-intake implementation from config: Service Bus when configured, else in-process.</summary>
public static class OrderIntakeServiceCollectionExtensions
{
    public static IServiceCollection AddOrderIntake(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OrderIntakeOptions>(configuration.GetSection(OrderIntakeOptions.SectionName));

        var connection = configuration["ServiceBusConnection"];
        var fullyQualifiedNamespace = configuration["ServiceBusConnection:fullyQualifiedNamespace"];

        if (!string.IsNullOrWhiteSpace(connection))
        {
            services.TryAddSingleton(_ => new ServiceBusClient(connection));
            services.AddSingleton<IOrderIntake, ServiceBusOrderIntake>();
        }
        else if (!string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
        {
            services.TryAddSingleton(_ => new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential()));
            services.AddSingleton<IOrderIntake, ServiceBusOrderIntake>();
        }
        else
        {
            // Dev host / tests: no broker — run the writeback inline (requires AddWriteback).
            services.AddSingleton<IOrderIntake, InProcessOrderIntake>();
        }

        return services;
    }
}
