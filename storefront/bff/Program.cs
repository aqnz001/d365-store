using PartsPortal.Bff.Auth;
using PartsPortal.Bff.Clients;

var builder = WebApplication.CreateBuilder(args);

builder.AddStorefrontAuth();
builder.Services.AddBffClients(builder.Configuration);

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

// Who am I (the authenticated B2B customer).
api.MapGet("/me", (HttpContext context) =>
    Results.Ok(new { customerAccount = context.User.FindFirst(DevAuthenticationHandler.CustomerClaim)?.Value }));

// Catalog browse (S2) — BYOD-synced storefront catalog (Golden Rule #3).
api.MapGet("/catalog", async (ICatalogApi catalog, CancellationToken ct) =>
    Results.Ok(await catalog.ListAsync(ct)));

api.MapGet("/catalog/{sku}", async (string sku, ICatalogApi catalog, CancellationToken ct) =>
    await catalog.GetAsync(sku, ct) is { } product ? Results.Ok(product) : Results.NotFound());

app.Run();

namespace PartsPortal.Bff
{
    /// <summary>Public entry-point marker for WebApplicationFactory in tests.</summary>
    public sealed class BffApp;
}
