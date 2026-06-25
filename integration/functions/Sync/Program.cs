using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PartsPortal.Shared.Http;
using PartsPortal.Sync;

// Isolated-worker host: resilient, config-driven external HttpClients (T4) + the catalog
// sync services (T5). Endpoints come from the "ExternalEndpoints" configuration section.
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddExternalHttpClients(context.Configuration);
        services.AddCatalogSync();
    })
    .Build();

host.Run();
