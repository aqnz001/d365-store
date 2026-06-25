using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PartsPortal.Shared.Availability;
using PartsPortal.Shared.Http;

// Isolated-worker host: resilient, config-driven external HttpClients (T4) + the cart
// availability stack (T6). Endpoints from "ExternalEndpoints"; IVS/band config from
// "Ivs"/"Availability".
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddExternalHttpClients(context.Configuration);
        services.AddAvailability(context.Configuration);
    })
    .Build();

host.Run();
