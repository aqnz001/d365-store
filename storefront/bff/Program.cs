using PartsPortal.Bff;
using PartsPortal.Bff.Account;
using PartsPortal.Bff.Auth;
using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Checkout;
using PartsPortal.Bff.Clients;
using PartsPortal.Bff.Payments;
using PartsPortal.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

// Phase 2: load secrets (Entra client secret, Stripe keys) from Key Vault via managed identity
// (Golden Rule #9) when configured. No secrets in config/code.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new Azure.Identity.DefaultAzureCredential());
}

builder.AddStorefrontAuth();
builder.Services.AddBffClients(builder.Configuration);
builder.Services.AddBffServices(builder.Configuration);

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

api.MapDelete("/cart/items/{index:int}", (int index, HttpContext context, CartService cart) =>
    Results.Ok(cart.RemoveAt(Customer(context), index)));

api.MapDelete("/cart", (HttpContext context, CartService cart) =>
{
    cart.Clear(Customer(context));
    return Results.NoContent();
});

api.MapPost("/cart/validate", async (HttpContext context, CartService cart, CancellationToken ct) =>
    Results.Ok(await cart.ValidateAsync(Customer(context), Correlation(context), ct)));

// Checkout gate (S4) — availability → price/credit → soft reservation, before payment.
api.MapPost("/checkout/start", async (HttpContext context, CheckoutService checkout, CancellationToken ct) =>
    Results.Ok(await checkout.StartAsync(Customer(context), Correlation(context), ct)));

// Payment (S5) — authorize, then submit the order for queue-backed writeback.
api.MapPost("/checkout/pay", async (HttpContext context, PayRequest request, PaymentService payment, CancellationToken ct) =>
    Results.Ok(await payment.PayAsync(Customer(context), request, Correlation(context), ct)));

// Account & B2B (S6) — order history, live order status, and credit/net-terms standing.
api.MapGet("/account", (HttpContext context) =>
    Results.Ok(new { customerAccount = Customer(context) }));

api.MapGet("/account/orders", (HttpContext context, AccountService account) =>
    Results.Ok(account.GetOrders(Customer(context))));

api.MapGet("/account/orders/{reference}/status", async (string reference, HttpContext context, AccountService account, CancellationToken ct) =>
    await account.GetOrderStatusAsync(reference, Correlation(context), ct) is { } status ? Results.Ok(status) : Results.NotFound());

api.MapGet("/account/credit", async (HttpContext context, AccountService account, CancellationToken ct) =>
    Results.Ok(await account.GetCreditStandingAsync(Customer(context), Correlation(context), ct)));

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
