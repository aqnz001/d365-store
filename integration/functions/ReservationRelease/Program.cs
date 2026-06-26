using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PartsPortal.Shared.Http;
using PartsPortal.Shared.Reservations;

// Isolated-worker host: resilient, config-driven external HttpClients (T4) + the reservation
// registry and TTL release job (T12). Endpoints from "ExternalEndpoints"; TTL from "Ivs".
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddExternalHttpClients(context.Configuration);
        services.AddReservationRelease(context.Configuration);
    })
    .Build();

host.Run();
