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
public sealed class CheckoutService(ICartStore cartStore, ICatalogApi catalog, IMiddlewareApi middleware, ICheckoutSessionStore sessions)
{
    public async Task<CheckoutResult> StartAsync(string customerAccount, string correlationId, CancellationToken ct = default)
    {
        var cart = cartStore.Get(customerAccount);

        // Any prior gate result is stale once a new gate run begins — payment must only ever act on
        // the reservation set this run places (cleared here, set on the Ready path below).
        sessions.Clear(customerAccount);

        // 1. Live availability check (authoritative). Catalog attributes (backorderable/discontinued)
        // are joined in so the band reflects them — same builder the cart page uses.
        var validateRequest = await CartService.BuildValidateRequestAsync(customerAccount, cart, catalog, ct);
        var availability = await middleware.ValidateCartAsync(validateRequest, correlationId, ct);
        if (availability.Lines.Any(line => line.Decision == LineDecision.Block))
        {
            return new CheckoutResult(CheckoutStatus.AvailabilityBlocked, [], null, availability, "One or more lines are unavailable.", AllowOnAccount: false);
        }

        // 2. Lock prices + credit decision.
        var pricing = await middleware.ResolvePricingAsync(BuildPricing(customerAccount, cart), correlationId, ct);
        if (pricing.Decision == CreditDecision.Blocked)
        {
            return new CheckoutResult(CheckoutStatus.CreditBlocked, [], pricing, availability, "Account is on credit hold.", AllowOnAccount: false);
        }

        // 3. Soft reservation (all-or-nothing — DR-009).
        var (reserved, reserveResponse) = await middleware.ReserveAsync(BuildReserve(customerAccount, cart), correlationId, ct);
        if (!reserved)
        {
            return new CheckoutResult(CheckoutStatus.Shortfall, [], pricing, availability, "Insufficient availability to reserve the cart.", AllowOnAccount: false);
        }

        // Persist the reservation set server-side — payment reads it from here, never from the
        // client request (Golden Rule #5 / DR-020). Keyed by customer, like the cart.
        var reservationIds = reserveResponse.ReservationIds.ToList();
        sessions.Set(customerAccount, new CheckoutSession(reservationIds));

        // Net terms may be offered only when the credit decision is Approved (DR-019).
        var allowOnAccount = pricing.Decision == CreditDecision.Approved;
        return new CheckoutResult(CheckoutStatus.Ready, reservationIds, pricing, availability, null, allowOnAccount);
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
