using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace TaxVision.Mobile.Tests.Architecture;

public sealed class MobileRoutingHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Server=127.0.0.1,1;Database=unused;Trusted_Connection=False");
        builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false");
        builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1");
        builder.ConfigureTestServices(services =>
        {
            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();
            // Fuera el worker y las reconciliaciones (propias y del kit de visibilidad).
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                var ns = hosted.ImplementationType?.Namespace;
                if (
                    ns?.StartsWith("TaxVision.", StringComparison.Ordinal) == true
                    || ns?.StartsWith("BuildingBlocks.CustomerVisibility", StringComparison.Ordinal) == true
                )
                    services.Remove(hosted);
            }
        });
    }
}
