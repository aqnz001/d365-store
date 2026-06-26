using PartsPortal.Bff.Clients;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Pricing;

namespace PartsPortal.Bff.Account;

/// <summary>The customer's net-terms / credit standing (TDD §9; DR-007 B2B scope).</summary>
public sealed record CreditStanding(string CustomerAccount, string CreditStatus, CreditDecision Decision);

/// <summary>
/// B2B account features (DR-007): order history (storefront mirror), live order status (via the
/// middleware), and credit/net-terms standing (resolved from the pricing/credit service).
/// </summary>
public sealed class AccountService(IOrderHistoryStore history, IMiddlewareApi middleware)
{
    public IReadOnlyList<PlacedOrder> GetOrders(string customerAccount) => history.GetOrders(customerAccount);

    public Task<OrderStatusResponse?> GetOrderStatusAsync(string orderReference, string correlationId, CancellationToken ct = default) =>
        middleware.GetOrderStatusAsync(orderReference, correlationId, ct);

    public async Task<CreditStanding> GetCreditStandingAsync(string customerAccount, string correlationId, CancellationToken ct = default)
    {
        // Resolve credit with no lines — just the customer's standing.
        var pricing = await middleware.ResolvePricingAsync(new PricingResolveRequest(customerAccount, []), correlationId, ct);
        return new CreditStanding(customerAccount, pricing.CreditStatus, pricing.Decision);
    }
}
