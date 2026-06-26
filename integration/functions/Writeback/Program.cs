using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PartsPortal.Shared.Http;
using PartsPortal.Shared.Writeback;

// Isolated-worker host: resilient, config-driven external HttpClients (T4) + the order
// writeback stack (T9). Endpoints from "ExternalEndpoints"; IVS env from "Ivs".
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddExternalHttpClients(context.Configuration);
        services.AddWriteback(context.Configuration);
    })
    .Build();

host.Run();
