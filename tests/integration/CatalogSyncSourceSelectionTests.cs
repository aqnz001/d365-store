using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PartsPortal.Sync;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// The catalog source is config-selected (DR-005): the embedded sample by default, the Azure SQL
/// BYOD replica when CatalogSync:SourceMode=Sql — swapping is a settings change, no caller change.
/// </summary>
public class CatalogSyncSourceSelectionTests
{
    private static IByodCatalogSource Resolve(string? sourceMode)
    {
        var settings = sourceMode is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["CatalogSync:SourceMode"] = sourceMode };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddCatalogSync(configuration);
        return services.BuildServiceProvider().GetRequiredService<IByodCatalogSource>();
    }

    [Fact]
    public void Defaults_to_the_sample_source() =>
        Assert.IsType<SampleByodCatalogSource>(Resolve(null));

    [Fact]
    public void Sql_mode_selects_the_sql_byod_source() =>
        Assert.IsType<SqlByodCatalogSource>(Resolve("Sql"));
}
