using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;

// Isolated-worker host. DI / options binding for endpoints + resilient clients lands in T4.
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .Build();

host.Run();
