using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Checkout;
using PartsPortal.Bff.Payments;

namespace PartsPortal.Bff;

/// <summary>Registers the BFF's storefront services (cart, checkout, payments).</summary>
public static class BffServicesExtensions
{
    public static IServiceCollection AddBffServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ICartStore, InMemoryCartStore>();
        services.AddScoped<CartService>();
        services.AddScoped<CheckoutService>();

        // Payment provider selected by config (DR-003: Stripe in prod, Fake for Phase 1/test).
        services.AddSingleton<IPaymentProvider>(_ =>
            string.Equals(configuration["Payments:Provider"], "Stripe", StringComparison.OrdinalIgnoreCase)
                ? new StripePaymentProvider()
                : new FakePaymentProvider());
        services.AddScoped<PaymentService>();
        return services;
    }
}
