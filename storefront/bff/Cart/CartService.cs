using PartsPortal.Bff.Clients;
using PartsPortal.Shared.Contracts.Middleware;

namespace PartsPortal.Bff.Cart;

/// <summary>
/// Cart operations. Add-time enforces B2B order rules from the catalog metafields
/// (min-order-qty, order-multiple — TDD §5.1/§6.8); availability bands come live from the
/// middleware at validate time (the live check is authoritative — Golden Rule #5).
/// </summary>
public sealed class CartService(ICartStore store, ICatalogApi catalog, IMiddlewareApi middleware)
{
    public ShoppingCart Get(string customerAccount) => store.Get(customerAccount);

    public ShoppingCart RemoveAt(string customerAccount, int index)
    {
        store.RemoveAt(customerAccount, index);
        return store.Get(customerAccount);
    }

    public void Clear(string customerAccount) => store.Clear(customerAccount);

    public async Task<AddItemResult> AddItemAsync(string customerAccount, AddItemRequest request, CancellationToken ct = default)
    {
        var product = await catalog.GetAsync(request.ItemNumber, ct);
        if (product is null)
        {
            return AddItemResult.Invalid($"Unknown item '{request.ItemNumber}'.");
        }

        var metafields = product.Metafields;
        if (request.Quantity < metafields.MinOrderQty)
        {
            return AddItemResult.Invalid($"Minimum order quantity for {request.ItemNumber} is {metafields.MinOrderQty}.");
        }

        if (metafields.OrderMultiple > 0 && request.Quantity % metafields.OrderMultiple != 0)
        {
            return AddItemResult.Invalid($"Quantity for {request.ItemNumber} must be a multiple of {metafields.OrderMultiple}.");
        }

        store.Add(customerAccount, new CartLine(request.ItemNumber, request.Quantity, request.Site));
        return AddItemResult.Ok(store.Get(customerAccount));
    }

    /// <summary>Live availability validation of the whole cart (bands + decisions) via the middleware.</summary>
    public async Task<CartValidateResponse> ValidateAsync(string customerAccount, string correlationId, CancellationToken ct = default)
    {
        var request = await BuildValidateRequestAsync(customerAccount, store.Get(customerAccount), catalog, ct);
        return await middleware.ValidateCartAsync(request, correlationId, ct);
    }

    /// <summary>
    /// Builds the validate request, joining each line with its catalog attributes (backorderable /
    /// discontinued) so the band calculator can apply them (TDD §7.2; the live ATP read stays
    /// authoritative — Golden Rule #5). Shared by the cart and the checkout gate.
    /// </summary>
    public static async Task<CartValidateRequest> BuildValidateRequestAsync(
        string customerAccount, ShoppingCart cart, ICatalogApi catalog, CancellationToken ct = default)
    {
        var request = new CartValidateRequest { Customer = new CustomerRef { CustomerAccount = customerAccount } };
        foreach (var line in cart.Lines)
        {
            var product = await catalog.GetAsync(line.ItemNumber, ct);
            request.Lines.Add(new CartLineInput
            {
                ItemNumber = line.ItemNumber,
                Quantity = (double)line.Quantity,
                Site = line.Site,
                Backorderable = product?.Metafields.Backorderable ?? false,
                Discontinued = product is not null && !string.Equals(product.Status, "active", StringComparison.OrdinalIgnoreCase),
            });
        }

        return request;
    }
}
