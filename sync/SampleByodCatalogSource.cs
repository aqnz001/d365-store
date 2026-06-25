using System.Reflection;
using System.Text.Json;

namespace PartsPortal.Sync;

/// <summary>
/// Phase-1 deterministic <see cref="IByodCatalogSource"/> backed by an embedded sample catalog
/// (<c>sample-byod-catalog.json</c>). Lets T5 exercise the BYOD → Shopify sync (including
/// delisting of non-Active lifecycle states) with no live BYOD/Azure SQL dependency.
/// Phase 2 swaps in an Azure SQL implementation behind the same interface with no caller change
/// (Golden Rule #1). No hardcoded file paths — the catalog ships as a manifest resource.
/// </summary>
public sealed class SampleByodCatalogSource : IByodCatalogSource
{
    private const string ResourceFileName = "sample-byod-catalog.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Assembly _assembly;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<ByodProduct>? _cached;

    public SampleByodCatalogSource()
        : this(typeof(SampleByodCatalogSource).Assembly)
    {
    }

    // Test seam: lets callers point at a specific assembly carrying the resource.
    internal SampleByodCatalogSource(Assembly assembly)
    {
        _assembly = assembly;
    }

    public async Task<IReadOnlyList<ByodProduct>> ReadCatalogAsync(CancellationToken ct = default)
    {
        // Fast path: already parsed.
        if (_cached is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Re-check under the lock in case another caller populated the cache while we waited.
            if (_cached is { } existing)
            {
                return existing;
            }

            _cached = await LoadAsync(ct).ConfigureAwait(false);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ByodProduct>> LoadAsync(CancellationToken ct)
    {
        var resourceName = ResolveResourceName();

        await using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded sample BYOD catalog resource '{resourceName}' was not found in assembly '{_assembly.FullName}'.");

        var products = await JsonSerializer
            .DeserializeAsync<List<ByodProduct>>(stream, SerializerOptions, ct)
            .ConfigureAwait(false);

        if (products is null)
        {
            throw new InvalidOperationException(
                $"Embedded sample BYOD catalog resource '{resourceName}' deserialized to null.");
        }

        return products;
    }

    private string ResolveResourceName()
    {
        var names = _assembly.GetManifestResourceNames();
        foreach (var name in names)
        {
            if (name.EndsWith(ResourceFileName, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        throw new InvalidOperationException(
            $"No embedded resource ending in '{ResourceFileName}' was found in assembly '{_assembly.FullName}'. " +
            "Ensure 'sample-byod-catalog.json' is declared as an EmbeddedResource in the Sync project.");
    }
}
