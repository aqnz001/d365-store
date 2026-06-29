using PartsPortal.Bff.Clients;

namespace PartsPortal.Bff.Catalog;

/// <summary>A page of catalog results plus the total and the full category list (so the SPA's
/// filter chips stay complete even when only one page is shown).</summary>
public sealed record CatalogPage(
    IReadOnlyList<CatalogProduct> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<string> Categories);

/// <summary>
/// Server-side catalog search/filter/sort/pagination (prod-readiness #4): the SPA requests a page
/// with a query instead of loading the whole catalog and filtering in the browser. Phase 1 filters
/// the BYOD-synced list in the BFF; Phase 2 pushes the predicate down to the catalog read-store.
/// </summary>
public sealed class CatalogService(ICatalogApi catalog)
{
    public const int DefaultPageSize = 12;
    private const int MaxPageSize = 60;

    public async Task<CatalogPage> SearchAsync(
        string? query,
        string? category,
        string? sort,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var all = (await catalog.ListAsync(ct))
            .Where(p => string.Equals(p.Status, "active", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // The full category set comes from the whole catalog, not just the current page/filter.
        var categories = all
            .Select(p => p.ProductType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        IEnumerable<CatalogProduct> filtered = all;
        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(p => string.Equals(p.ProductType, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            filtered = filtered.Where(p =>
                p.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        filtered = sort switch
        {
            "title" => filtered.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase),
            "category" => filtered.OrderBy(p => p.ProductType, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase),
            _ => filtered, // "featured" — preserve the catalog's source order
        };

        var matches = filtered.ToList();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        var items = matches.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new CatalogPage(items, matches.Count, page, pageSize, categories);
    }
}
