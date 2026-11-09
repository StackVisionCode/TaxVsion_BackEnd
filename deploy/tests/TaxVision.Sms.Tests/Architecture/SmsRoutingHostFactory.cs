using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace TaxVision.Sms.Tests.Architecture;

/// <summary>
/// Host real de Sms solo para leer las rutas de Wolverine: sin base, sin Rabbit y sin jobs. No se conecta a nada,
/// así que corre en el gate del CI.
/// </summary>
public sealed class SmsRoutingHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Server=127.0.0.1,1;Database=unused;Trusted_Connection=False");
        builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false");
        builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1");
        builder.UseSetting("Jwt:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.UseSetting("Sms:Providers:fake:Enabled", "true");
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
