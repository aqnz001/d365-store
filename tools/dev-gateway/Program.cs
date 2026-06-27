using System.Text.Json.Serialization;
using PartsPortal.Shared.Availability;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Http;
using PartsPortal.Shared.Pricing;
using PartsPortal.Shared.Status;
using PartsPortal.Shared.Writeback;
using MsgMoney = PartsPortal.Shared.Contracts.Messages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddExternalHttpClients(builder.Configuration);
builder.Services.AddAvailability(builder.Configuration);
builder.Services.AddPricingCredit();
builder.Services.AddWriteback(builder.Configuration);
builder.Services.AddOrderIntake(builder.Configuration); // in-process here (no Service Bus)
// Status mirror + in-process events emitter (no Service Bus in this dev host).
builder.Services.AddStatusSync(builder.Configuration);
builder.Services.AddStatusEventPublisher(builder.Configuration);
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

// /order — same intake seam as production; in-process here so it runs the writeback inline.
app.MapPost("/order", async (OrderRequest request, IOrderIntake intake, HttpContext ctx) =>
    Results.Accepted(value: await intake.SubmitAsync(request, ctx.RequestAborted)));

// Simulate a FinOps fulfilment business event (pack/ship/invoice/return/cancel) flowing back
// through the status pipeline — the in-process emitter applies it to the order-status mirror.
app.MapPost("/dev/status-event", async (MsgMoney.FulfilmentStatusEvent statusEvent, IStatusEventPublisher publisher, HttpContext ctx) =>
{
    await publisher.PublishAsync(statusEvent, ctx.RequestAborted);
    return Results.Accepted();
});

// Live order status the BFF reads (GET order/{id}/status) — mirrors the storefront status view.
app.MapGet("/order/{reference}/status", (string reference, IOrderStatusStore store) =>
    store.Get(reference) is { } view ? Results.Ok(OrderStatusMapper.ToResponse(view)) : Results.NotFound());

app.Run();

static string Correlation(HttpContext ctx) =>
    ctx.Request.Headers.TryGetValue("x-correlation-id", out var value) && !string.IsNullOrWhiteSpace(value)
        ? value.ToString()
        : Guid.NewGuid().ToString("N");

namespace PartsPortal.Tools.DevGateway
{
    public sealed class DevGatewayApp;
}
