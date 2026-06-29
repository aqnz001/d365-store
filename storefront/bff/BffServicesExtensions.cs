using PartsPortal.Bff.Account;
using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Checkout;
using PartsPortal.Bff.Payments;
using PartsPortal.Shared.Caching;

namespace PartsPortal.Bff;

/// <summary>Registers the BFF's storefront services (cart, checkout, payments, account).</summary>
public static class BffServicesExtensions
{
    public static IServiceCollection AddBffServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Cart + order history: durable over Redis when configured (DR-011) so they survive BFF
        // restarts / scale-out; in-memory for Phase 1 / dev / test.
        if (DistributedCacheBackend.IsRedisConfigured(configuration))
        {
            services.AddPortalDistributedCache(configuration);
            services.AddSingleton<ICartStore, DistributedCartStore>();
            services.AddSingleton<IOrderHistoryStore, DistributedOrderHistoryStore>();
        }
        else
        {
            services.AddSingleton<ICartStore, InMemoryCartStore>();
            services.AddSingleton<IOrderHistoryStore, InMemoryOrderHistoryStore>();
        }

        // Server-held checkout-gate state (the soft-reservation set) so payment never trusts the
        // client to echo reservation ids back (Golden Rule #5 / DR-020). In-memory for Phase 1.
        services.AddSingleton<ICheckoutSessionStore, InMemoryCheckoutSessionStore>();

        services.AddScoped<Catalog.CatalogService>();
        services.AddScoped<CartService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<AccountService>();

        // Payment provider selected by config (DR-003: Stripe in prod, Fake for Phase 1/test).
        services.AddSingleton<IPaymentProvider>(_ =>
            string.Equals(configuration["Payments:Provider"], "Stripe", StringComparison.OrdinalIgnoreCase)
                ? new StripePaymentProvider(configuration)
                : new FakePaymentProvider());
        services.AddScoped<PaymentService>();
        return services;
    }
}
