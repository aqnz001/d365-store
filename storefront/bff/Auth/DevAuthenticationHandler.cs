using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PartsPortal.Bff.Auth;

/// <summary>
/// Local/test authentication: treats every request as a B2B customer taken from the
/// <c>X-Dev-Customer</c> header (default "C-DEV"). Production uses Entra SSO (DR-004); this
/// scheme is only active when Auth:Mode != "Entra" so the BFF runs and is testable without
/// a live identity provider. NEVER enable in production.
/// </summary>
public sealed class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Dev";
    public const string CustomerHeader = "X-Dev-Customer";
    public const string UserHeader = "X-Dev-User";
    public const string CustomerClaim = "customerAccount";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var customer = Request.Headers[CustomerHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(customer))
        {
            customer = "C-DEV";
        }

        // X-Dev-User simulates a distinct user within the company (for B2B roles); defaults to a
        // per-company address so a single-user company still works. In prod this is the Entra email.
        var user = Request.Headers[UserHeader].FirstOrDefault();
        var email = string.IsNullOrWhiteSpace(user) ? $"{customer.ToLowerInvariant()}@example.com" : user;

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, email),
                new Claim(ClaimTypes.Name, $"{customer} (dev)"),
                new Claim(ClaimTypes.Email, email),
                new Claim(CustomerClaim, customer),
            ],
            Scheme.Name);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
