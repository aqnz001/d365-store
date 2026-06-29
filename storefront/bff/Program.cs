using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using PartsPortal.Bff;
using PartsPortal.Bff.Account;
using PartsPortal.Bff.Auth;
using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Catalog;
using PartsPortal.Bff.Checkout;
using PartsPortal.Bff.Clients;
using PartsPortal.Bff.Payments;
using PartsPortal.Bff.Security;
using PartsPortal.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

// Auth mode drives the login/logout endpoints below: Entra triggers the OIDC challenge / end-session;
// Dev (local/test) is always authenticated via the X-Dev-Customer header, so they just redirect.
var entraMode = string.Equals(builder.Configuration["Auth:Mode"], "Entra", StringComparison.OrdinalIgnoreCase);

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
builder.Services.AddBffSecurity(builder.Configuration);

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

// Behind APIM/Front Door, honour X-Forwarded-For so the rate limiter sees the real client IP
// (trusts only the proxy networks configured in ForwardedHeaders:KnownNetworks — empty = loopback).
app.UseForwardedHeaders();

// Security headers (incl. CSP) on every response; HSTS outside Development.
app.UseBffSecurityHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpLogging();
app.UseCors(SpaCorsPolicy);
app.UseAuthentication();
// Rate limiter AFTER authentication so the per-customer partition key (the customer claim) is
// populated; anonymous endpoints fall back to the (forwarded) client IP.
app.UseRateLimiter();
app.UseAuthorization();

// Health/readiness probe is exempt from rate limiting (orchestrators poll it frequently).
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "bff" })).DisableRateLimiting();

// Auth UX (anonymous): the SPA's Sign in / Sign out controls call these. Login triggers the Entra
// OIDC challenge (Dev just returns, already authenticated); logout clears the cookie + Entra session.
app.MapGet("/api/auth/login", (string? returnUrl) =>
{
    var target = LocalReturn(returnUrl);
    return entraMode
        ? Results.Challenge(new AuthenticationProperties { RedirectUri = target }, [OpenIdConnectDefaults.AuthenticationScheme])
        : Results.Redirect(target);
}).AllowAnonymous().RequireRateLimiting(SecurityExtensions.SensitivePolicy);

// POST (not GET) so a cross-site link/image/prefetch cannot force a logout: with the cookie's
// default SameSite=Lax, the session cookie is not sent on a cross-site POST (CSRF-safe).
app.MapPost("/api/auth/logout", () =>
{
    if (!entraMode)
    {
        return Results.Redirect("/");
    }

    return Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
}).AllowAnonymous().RequireRateLimiting(SecurityExtensions.SensitivePolicy);

var api = app.MapGroup("/api").RequireAuthorization();

api.MapGet("/me", (HttpContext context) =>
    Results.Ok(new
    {
        customerAccount = Customer(context),
        name = context.User.Identity?.Name ?? context.User.FindFirst("name")?.Value ?? Customer(context),
        email = context.User.FindFirst(ClaimTypes.Email)?.Value
            ?? context.User.FindFirst("preferred_username")?.Value
            ?? context.User.FindFirst("emails")?.Value,
    }));

// Catalog browse (S2) — BYOD-synced storefront catalog (Golden Rule #3).
// Full list (used by the cart/drawer to resolve line titles).
api.MapGet("/catalog", async (ICatalogApi catalog, CancellationToken ct) =>
    Results.Ok(await catalog.ListAsync(ct)));

// Server-side search/filter/sort/pagination (#4) — the browse page requests a page, not the whole
// catalog. The literal "search" segment takes routing precedence over the {sku} parameter below.
api.MapGet("/catalog/search", async (
    string? q,
    string? category,
    string? sort,
    int? page,
    int? pageSize,
    CatalogService catalog,
    CancellationToken ct) =>
    Results.Ok(await catalog.SearchAsync(q, category, sort, page ?? 1, pageSize ?? CatalogService.DefaultPageSize, ct)));

// Detail lives under /item/{sku} so no SKU value can collide with the literal "search" sibling.
api.MapGet("/catalog/item/{sku}", async (string sku, ICatalogApi catalog, CancellationToken ct) =>
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

// Payment (S5) — authorize, then submit the order for queue-backed writeback. Stricter rate limit:
// payment is the most abuse-sensitive endpoint.
api.MapPost("/checkout/pay", async (HttpContext context, PayRequest request, PaymentService payment, CancellationToken ct) =>
    Results.Ok(await payment.PayAsync(Customer(context), Email(context), request, Correlation(context), ct)))
    .RequireRateLimiting(SecurityExtensions.SensitivePolicy);

// Account & B2B (S6) — order history, live order status, and credit/net-terms standing.
api.MapGet("/account", (HttpContext context) =>
    Results.Ok(new { customerAccount = Customer(context) }));

api.MapGet("/account/orders", (HttpContext context, AccountService account) =>
    Results.Ok(account.GetOrders(Customer(context))));

api.MapGet("/account/orders/{reference}/status", async (string reference, HttpContext context, AccountService account, CancellationToken ct) =>
    await account.GetOrderStatusAsync(reference, Correlation(context), ct) is { } status ? Results.Ok(status) : Results.NotFound());

api.MapGet("/account/credit", async (HttpContext context, AccountService account, CancellationToken ct) =>
    Results.Ok(await account.GetCreditStandingAsync(Customer(context), Correlation(context), ct)));

// Address book (#5) — saved shipping/billing addresses, CRUD, per customer.
api.MapGet("/account/addresses", (HttpContext context, AddressService addresses) =>
    Results.Ok(addresses.List(Customer(context))));

api.MapPost("/account/addresses", (HttpContext context, AddressInput input, AddressService addresses) =>
{
    var result = addresses.Add(Customer(context), input);
    return result.Ok
        ? Results.Created($"/api/account/addresses/{result.Address!.Id}", result.Address)
        : Results.BadRequest(new { message = result.Error });
});

api.MapPut("/account/addresses/{id}", (string id, HttpContext context, AddressInput input, AddressService addresses) =>
{
    var result = addresses.Update(Customer(context), id, input);
    if (result.Ok)
    {
        return Results.Ok(result.Address);
    }

    return result.Error == "Address not found." ? Results.NotFound() : Results.BadRequest(new { message = result.Error });
});

api.MapDelete("/account/addresses/{id}", (string id, HttpContext context, AddressService addresses) =>
    addresses.Remove(Customer(context), id) ? Results.NoContent() : Results.NotFound());

app.Run();

// Only allow returning to a same-origin relative path (defends against open-redirect, CWE-601, on
// the login returnUrl). Reject control/whitespace characters: browsers strip tab/CR/LF from URLs
// before parsing, so "/\t//evil.com" would otherwise resolve to the cross-origin "//evil.com".
static string LocalReturn(string? returnUrl)
{
    if (string.IsNullOrEmpty(returnUrl) || returnUrl[0] != '/')
    {
        return "/";
    }

    // Scheme-relative ("//host") or backslash ("/\host") forms escape the origin.
    if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\'))
    {
        return "/";
    }

    return returnUrl.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ? "/" : returnUrl;
}

static string Customer(HttpContext context) =>
    context.User.FindFirst(DevAuthenticationHandler.CustomerClaim)?.Value ?? "C-DEV";

static string? Email(HttpContext context) =>
    context.User.FindFirst(ClaimTypes.Email)?.Value
    ?? context.User.FindFirst("preferred_username")?.Value
    ?? context.User.FindFirst("emails")?.Value;

static string Correlation(HttpContext context) =>
    context.Request.Headers.TryGetValue(CorrelationContext.HeaderName, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value.ToString()
        : CorrelationContext.New().CorrelationId;

namespace PartsPortal.Bff
{
    /// <summary>Public entry-point marker for WebApplicationFactory in tests.</summary>
    public sealed class BffApp;
}
