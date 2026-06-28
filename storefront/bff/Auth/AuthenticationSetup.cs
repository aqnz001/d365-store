using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Hosting;

namespace PartsPortal.Bff.Auth;

/// <summary>
/// Storefront auth. Production = Microsoft Entra SSO via OIDC at the BFF (DR-004): OIDC
/// auth-code flow, tokens held in the server-side cookie session — never in the browser
/// (Golden Rule #11). Local/test = the Dev scheme. Selected by Auth:Mode.
/// </summary>
public static class AuthenticationSetup
{
    public static void AddStorefrontAuth(this WebApplicationBuilder builder)
    {
        var mode = builder.Configuration["Auth:Mode"] ?? DevAuthenticationHandler.SchemeName;

        if (string.Equals(mode, "Entra", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                })
                .AddCookie()
                .AddOpenIdConnect(options =>
                {
                    // Entra authority, e.g. https://login.microsoftonline.com/{tenantId}/v2.0
                    options.Authority = builder.Configuration["Auth:Entra:Authority"];
                    options.ClientId = builder.Configuration["Auth:Entra:ClientId"];
                    // Secret/cert provisioned via Key Vault + managed identity (Golden Rule #9);
                    // never committed. Workload identity / certificate preferred over a secret.
                    options.ClientSecret = builder.Configuration["Auth:Entra:ClientSecret"];
                    options.ResponseType = "code";
                    options.SaveTokens = true;
                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                });
        }
        else
        {
            // Fail-closed (Golden Rule #9, DR-004): the Dev scheme authenticates EVERY request as a
            // B2B customer from the X-Dev-Customer header with no credentials, so it must never run in
            // a real production deployment. The blessed deploy sets Auth:Mode=Entra; this guard catches
            // the fail-open case where that setting is dropped/typo'd under a Production environment.
            // An operator who deliberately wants a production-like local stack must opt in explicitly.
            var allowDevInProduction = builder.Configuration.GetValue("Auth:AllowDevAuthInProduction", false);
            if (builder.Environment.IsProduction() && !allowDevInProduction)
            {
                throw new InvalidOperationException(
                    "Dev authentication scheme selected under the Production environment. Set Auth:Mode=Entra for " +
                    "production, or Auth:AllowDevAuthInProduction=true for a deliberate production-like local stack.");
            }

            builder.Services
                .AddAuthentication(DevAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthenticationHandler.SchemeName, _ => { });
        }

        builder.Services.AddAuthorization();
    }
}
