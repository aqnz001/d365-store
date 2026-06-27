using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;

namespace PartsPortal.Shared.Http;

/// <summary>
/// Attaches an Entra bearer token to outbound calls for one external client (Phase 2). The token is
/// acquired by client-credentials (TenantId/ClientId/ClientSecret) or managed identity, for the
/// scope configured for this client. When no scope is configured for the client the handler is a
/// pass-through — so the Phase-1 mocks (no auth) keep working with no config. Azure.Identity caches
/// and refreshes the token internally.
/// </summary>
public sealed class EntraTokenHandler : DelegatingHandler
{
    private readonly TokenCredential? _credential;
    private readonly string[]? _scopes;

    public EntraTokenHandler(ExternalAuthOptions options, string clientName)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Scopes.TryGetValue(clientName, out var scope) && !string.IsNullOrWhiteSpace(scope))
        {
            _scopes = [scope];
            _credential = options.UseManagedIdentity || string.IsNullOrWhiteSpace(options.ClientSecret)
                ? new DefaultAzureCredential()
                : new ClientSecretCredential(options.TenantId, options.ClientId, options.ClientSecret);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_credential is not null && _scopes is not null)
        {
            var token = await _credential.GetTokenAsync(new TokenRequestContext(_scopes), cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
