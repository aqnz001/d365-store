using PartsPortal.Bff;
using PartsPortal.Bff.Auth;
using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Checkout;
using PartsPortal.Bff.Clients;
using PartsPortal.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

builder.AddStorefrontAuth();
builder.Services.AddBffClients(builder.Configuration);
builder.Services.AddBffServices();

// Serialize enums as strings in API responses (e.g. checkout status, availability bands).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

const string SpaCorsPolicy = "spa";
builder.Services.AddCors(options => options.AddPolicy(SpaCorsPolicy, policy =>
{
    var origin = builder.Configuration["Bff:SpaOrigin"];
    if (!string.IsNullOrWhiteSpace(origin))
    {
        policy.WithOrigins(origin).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }
}));

var app = builder.Build();

app.UseCors(SpaCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "bff" }));

var api = app.MapGroup("/api").RequireAuthorization();

api.MapGet("/me", (HttpContext context) =>
    Results.Ok(new { customerAccount = Customer(context) }));

// Catalog browse (S2) — BYOD-synced storefront catalog (Golden Rule #3).
api.MapGet("/catalog", async (ICatalogApi catalog, CancellationToken ct) =>
    Results.Ok(await catalog.ListAsync(ct)));

api.MapGet("/catalog/{sku}", async (string sku, ICatalogApi catalog, CancellationToken ct) =>
    await catalog.GetAsync(sku, ct) is { } product ? Results.Ok(product) : Results.NotFound());

// Cart (S3) — order rules enforced on add; availability validated live.
api.MapGet("/cart", (HttpContext context, CartService cart) =>
    Results.Ok(cart.Get(Customer(context))));

api.MapPost("/cart/items", async (HttpContext context, AddItemRequest request, CartService cart, CancellationToken ct) =>
{
    var result = await cart.AddItemAsync(Customer(context), request, ct);
    return result.Valid ? Results.Ok(result.Cart) : Results.BadRequest(new { message = result.Message });
});

api.MapPost("/cart/validate", async (HttpContext context, CartService cart, CancellationToken ct) =>
    Results.Ok(await cart.ValidateAsync(Customer(context), Correlation(context), ct)));

// Checkout gate (S4) — availability → price/credit → soft reservation, before payment.
api.MapPost("/checkout/start", async (HttpContext context, CheckoutService checkout, CancellationToken ct) =>
    Results.Ok(await checkout.StartAsync(Customer(context), Correlation(context), ct)));

app.Run();

static string Customer(HttpContext context) =>
    context.User.FindFirst(DevAuthenticationHandler.CustomerClaim)?.Value ?? "C-DEV";

static string Correlation(HttpContext context) =>
    context.Request.Headers.TryGetValue(CorrelationContext.HeaderName, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value.ToString()
        : CorrelationContext.New().CorrelationId;

namespace PartsPortal.Bff
{
    /// <summary>Public entry-point marker for WebApplicationFactory in tests.</summary>
    public sealed class BffApp;
}
