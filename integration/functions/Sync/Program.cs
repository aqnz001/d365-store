using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PartsPortal.Shared.Http;
using PartsPortal.Shared.Status;
using PartsPortal.Sync;

// Isolated-worker host: resilient external HttpClients (T4) + catalog sync (T5) + the
// fulfilment status sync (T10). Endpoints come from "ExternalEndpoints"; status events
// arrive on the status-outbound Service Bus topic.
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddExternalHttpClients(context.Configuration);
        services.AddCatalogSync();
        services.AddStatusSync(context.Configuration);
        // Fulfilment business-events emitter (Service Bus when configured, else in-process).
        services.AddStatusEventPublisher(context.Configuration);
    })
    .Build();

host.Run();
