using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PartsPortal.Sync;

/// <summary>
/// Phase-2 <see cref="IByodCatalogSource"/> reading the catalog from the BYOD Azure SQL replica of
/// the D365 product master (TDD §5.1; browse data is BYOD-only — Golden Rule #3). Connection +
/// query are config-driven (DR-005), so swapping the embedded sample for the real source is a
/// settings change. Build-verified; exercised against a real BYOD replica at deploy.
/// </summary>
public sealed class SqlByodCatalogSource(IOptions<CatalogSyncOptions> options) : IByodCatalogSource
{
    private readonly ByodSqlOptions _byod = options.Value.Byod;

    public async Task<IReadOnlyList<ByodProduct>> ReadCatalogAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_byod.ConnectionString))
        {
            throw new InvalidOperationException(
                "CatalogSync:SourceMode is 'Sql' but CatalogSync:Byod:ConnectionString is not configured.");
        }

        var products = new List<ByodProduct>();
        await using var connection = new SqlConnection(_byod.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = new SqlCommand(_byod.Query, connection);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            products.Add(new ByodProduct(
                Str(reader, "ItemNumber"),
                Str(reader, "ProductName"),
                Str(reader, "ProductDescription"),
                Str(reader, "RetailCategory"),
                Str(reader, "BaseUnit"),
                Dec(reader, "OrderMultiple"),
                Dec(reader, "MinOrderQty"),
                Bool(reader, "Backorderable"),
                Str(reader, "LifecycleState")));
        }

        return products;
    }

    private static string Str(SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? string.Empty : reader.GetValue(i).ToString() ?? string.Empty;
    }

    private static decimal Dec(SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? 0m : Convert.ToDecimal(reader.GetValue(i));
    }

    private static bool Bool(SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return !reader.IsDBNull(i) && Convert.ToBoolean(reader.GetValue(i));
    }
}
