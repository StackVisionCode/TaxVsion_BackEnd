using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace TaxVision.Reminder.Tests.Architecture;

/// <summary>
/// Host real de Reminder solo para leer las rutas de Wolverine: sin base, sin Rabbit, sin jobs ni Quartz. No se conecta
/// a nada, así que corre en el gate del CI.
/// </summary>
public sealed class ReminderRoutingHostFactory : WebApplicationFactory<Program>
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
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                // Quartz valida su esquema contra SQL al arrancar.
                var ns = hosted.ImplementationType?.Namespace;
                if (
                    ns?.StartsWith("TaxVision.", StringComparison.Ordinal) == true
                    || ns?.StartsWith("Quartz", StringComparison.Ordinal) == true
                )
                    services.Remove(hosted);
            }
        });
    }
}
