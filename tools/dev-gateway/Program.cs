using System.Text.Json.Serialization;
using PartsPortal.Shared.Availability;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Http;
using PartsPortal.Shared.Pricing;
using PartsPortal.Shared.Writeback;
using MsgMoney = PartsPortal.Shared.Contracts.Messages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddExternalHttpClients(builder.Configuration);
builder.Services.AddAvailability(builder.Configuration);
builder.Services.AddPricingCredit();
builder.Services.AddWriteback(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "dev-gateway" }));

app.MapPost("/cart/validate", async (CartValidateRequest request, ICartAvailabilityService availability, HttpContext ctx) =>
    Results.Ok(await availability.ValidateAsync(request, Correlation(ctx), ctx.RequestAborted)));

app.MapPost("/cart/reserve", async (ReserveRequest request, ICartAvailabilityService availability, HttpContext ctx) =>
{
    var (reserved, response) = await availability.ReserveAsync(request, Correlation(ctx), ctx.RequestAborted);
    return reserved ? Results.Ok(response) : Results.Json(response, statusCode: StatusCodes.Status409Conflict);
});

app.MapPost("/cart/release", async (ReleaseRequest request, ICartAvailabilityService availability, HttpContext ctx) =>
{
    await availability.ReleaseAsync(request, ctx.RequestAborted);
    return Results.NoContent();
});

app.MapPost("/pricing/resolve", async (PricingResolveRequest request, IPricingCreditService pricing, HttpContext ctx) =>
    Results.Ok(await pricing.ResolveAsync(request, ctx.RequestAborted)));

// /order runs the writeback in-process (no Service Bus in this dev host).
app.MapPost("/order", async (OrderRequest request, OrderWritebackService writeback, HttpContext ctx) =>
{
    var result = await writeback.ProcessAsync(ToInboundMessage(request), ctx.RequestAborted);
    return Results.Accepted(value: new OrderStatusResponse
    {
        OrderId = request.IdempotencyKey,
        SalesOrderNumber = result.SalesOrderNumber,
        Status = OrderStatus.WrittenBack,
        CorrelationId = request.CorrelationId,
        Message = result.Status.ToString(),
    });
});

app.Run();

static string Correlation(HttpContext ctx) =>
    ctx.Request.Headers.TryGetValue("x-correlation-id", out var value) && !string.IsNullOrWhiteSpace(value)
        ? value.ToString()
        : Guid.NewGuid().ToString("N");

static MsgMoney.OrderInboundMessage ToInboundMessage(OrderRequest request)
{
    var message = new MsgMoney.OrderInboundMessage
    {
        IdempotencyKey = request.IdempotencyKey,
        CorrelationId = request.CorrelationId,
        SessionId = request.Customer.CustomerAccount,
        CustomerAccount = request.Customer.CustomerAccount,
        Currency = request.Currency,
        PlacedAtUtc = DateTimeOffset.UtcNow,
    };

    var reservations = request.ReservationIds.ToList();
    var index = 0;
    foreach (var line in request.Lines)
    {
        message.Lines.Add(new MsgMoney.OrderLine
        {
            ItemNumber = line.ItemNumber,
            Quantity = line.Quantity,
            Unit = string.IsNullOrWhiteSpace(line.Unit) ? "ea" : line.Unit,
            Site = string.IsNullOrWhiteSpace(line.Site) ? "1" : line.Site,
            Backorder = line.Backorder,
            ReservationReference = index < reservations.Count ? reservations[index] : string.Empty,
            LockedPrice = new MsgMoney.Money(),
        });
        index++;
    }

    return message;
}

namespace PartsPortal.Tools.DevGateway
{
    public sealed class DevGatewayApp;
}
