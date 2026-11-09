using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace TaxVision.PaymentClient.Tests.Architecture;

/// <summary>
/// Host real de PaymentClient solo para leer las rutas de Wolverine: sin Rabbit, sin persistencia de mensajes y
/// sin jobs. Las cadenas apuntan a un puerto cerrado y nada las usa al arrancar, así que corre en el gate.
/// </summary>
public sealed class PaymentClientRoutingHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Server=127.0.0.1,1;Database=unused;Trusted_Connection=False");
        builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1");
        builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1");
        builder.UseSetting("Jwt:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.ConfigureTestServices(services =>
        {
            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                if (hosted.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal) == true)
                    services.Remove(hosted);
            }
        });
    }
}
