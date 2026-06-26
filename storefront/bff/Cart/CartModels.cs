namespace PartsPortal.Bff.Cart;

/// <summary>A line in the customer's cart.</summary>
public sealed record CartLine(string ItemNumber, decimal Quantity, string Site);

/// <summary>The customer's current cart (server-side; never browser storage — Golden Rule #11).</summary>
public sealed record ShoppingCart(IReadOnlyList<CartLine> Lines);

/// <summary>Request to add a line to the cart.</summary>
public sealed record AddItemRequest(string ItemNumber, decimal Quantity, string Site);

/// <summary>Outcome of adding to the cart (order-rule validation: min-qty / order-multiple, TDD §6.8).</summary>
public sealed record AddItemResult(bool Valid, ShoppingCart? Cart, string? Message)
{
    public static AddItemResult Ok(ShoppingCart cart) => new(true, cart, null);

    public static AddItemResult Invalid(string message) => new(false, null, message);
}
