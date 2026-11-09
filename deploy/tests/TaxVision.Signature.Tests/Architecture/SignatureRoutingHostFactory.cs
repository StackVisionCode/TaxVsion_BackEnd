using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace TaxVision.Signature.Tests.Architecture;

/// <summary>
/// Host real de Signature solo para leer las rutas y las reglas de fallo de Wolverine: sin base, sin Rabbit y
/// sin jobs. No se conecta a nada, así que corre en el gate del CI.
/// </summary>
public sealed class SignatureRoutingHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development: la clave de los enlaces de firma es efímera (fuera de Development exige un PEM).
        builder.UseEnvironment("Development");
        // UseSetting llega antes de que Program.cs lea la configuración; el in-memory gana sobre user-secrets.
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Server=127.0.0.1,1;Database=unused;Trusted_Connection=False",
            ["ConnectionStrings:Redis"] = "127.0.0.1:1,abortConnect=false",
            ["RabbitMq:Uri"] = "amqp://guest:guest@127.0.0.1:1",
            ["Jwt:PublicKeyPath"] = string.Empty,
            ["Jwt:PublicKeyPem"] = string.Empty,
            ["Jwt:Secret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
        };
        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        builder.ConfigureTestServices(services =>
        {
            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                var ns = hosted.ImplementationType?.Namespace;
                if (
                    ns?.StartsWith("TaxVision.", StringComparison.Ordinal) == true
                    || ns?.StartsWith("BuildingBlocks.", StringComparison.Ordinal) == true
                )
                    services.Remove(hosted);
            }
        });
    }
}
