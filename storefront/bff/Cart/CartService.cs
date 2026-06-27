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
    public Task<CartValidateResponse> ValidateAsync(string customerAccount, string correlationId, CancellationToken ct = default)
    {
        var request = new CartValidateRequest { Customer = new CustomerRef { CustomerAccount = customerAccount } };
        foreach (var line in store.Get(customerAccount).Lines)
        {
            request.Lines.Add(new CartLineInput { ItemNumber = line.ItemNumber, Quantity = (double)line.Quantity, Site = line.Site });
        }

        return middleware.ValidateCartAsync(request, correlationId, ct);
    }
}
