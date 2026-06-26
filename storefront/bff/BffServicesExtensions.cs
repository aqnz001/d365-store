using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Checkout;

namespace PartsPortal.Bff;

/// <summary>Registers the BFF's storefront services (cart + checkout).</summary>
public static class BffServicesExtensions
{
    public static IServiceCollection AddBffServices(this IServiceCollection services)
    {
        services.AddSingleton<ICartStore, InMemoryCartStore>();
        services.AddScoped<CartService>();
        services.AddScoped<CheckoutService>();
        return services;
    }
}
