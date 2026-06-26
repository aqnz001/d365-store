using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Clients;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Pricing;

namespace PartsPortal.Bff.Checkout;

/// <summary>
/// The checkout gate (TDD §6.1, Golden Rule #5): live availability → price/credit lock → soft
/// reservation, before payment. Blocks on unavailable lines, credit hold, or a shortfall; a
/// soft reservation is placed only when everything passes. Over-limit credit is allowed through
/// as approval-required (TDD §9). Payment + order submit follow in S5.
/// </summary>
public sealed class CheckoutService(ICartStore cartStore, IMiddlewareApi middleware)
{
    public async Task<CheckoutResult> StartAsync(string customerAccount, string correlationId, CancellationToken ct = default)
    {
        var cart = cartStore.Get(customerAccount);

        // 1. Live availability check (authoritative).
        var availability = await middleware.ValidateCartAsync(BuildValidate(customerAccount, cart), correlationId, ct);
        if (availability.Lines.Any(line => line.Decision == LineDecision.Block))
        {
            return new CheckoutResult(CheckoutStatus.AvailabilityBlocked, [], null, availability, "One or more lines are unavailable.");
        }

        // 2. Lock prices + credit decision.
        var pricing = await middleware.ResolvePricingAsync(BuildPricing(customerAccount, cart), correlationId, ct);
        if (pricing.Decision == CreditDecision.Blocked)
        {
            return new CheckoutResult(CheckoutStatus.CreditBlocked, [], pricing, availability, "Account is on credit hold.");
        }

        // 3. Soft reservation (all-or-nothing — DR-009).
        var (reserved, reserveResponse) = await middleware.ReserveAsync(BuildReserve(customerAccount, cart), correlationId, ct);
        if (!reserved)
        {
            return new CheckoutResult(CheckoutStatus.Shortfall, [], pricing, availability, "Insufficient availability to reserve the cart.");
        }

        return new CheckoutResult(CheckoutStatus.Ready, reserveResponse.ReservationIds.ToList(), pricing, availability, null);
    }

    private static CartValidateRequest BuildValidate(string customerAccount, ShoppingCart cart)
    {
        var request = new CartValidateRequest { Customer = new CustomerRef { CustomerAccount = customerAccount } };
        foreach (var line in cart.Lines)
        {
            request.Lines.Add(new CartLineInput { ItemNumber = line.ItemNumber, Quantity = (double)line.Quantity, Site = line.Site });
        }

        return request;
    }

    private static ReserveRequest BuildReserve(string customerAccount, ShoppingCart cart)
    {
        var request = new ReserveRequest { Customer = new CustomerRef { CustomerAccount = customerAccount } };
        foreach (var line in cart.Lines)
        {
            request.Lines.Add(new CartLineInput { ItemNumber = line.ItemNumber, Quantity = (double)line.Quantity, Site = line.Site });
        }

        return request;
    }

    private static PricingResolveRequest BuildPricing(string customerAccount, ShoppingCart cart) =>
        new(customerAccount, cart.Lines.Select(line => new PricingResolveLine(line.ItemNumber, line.Quantity)).ToList());
}
