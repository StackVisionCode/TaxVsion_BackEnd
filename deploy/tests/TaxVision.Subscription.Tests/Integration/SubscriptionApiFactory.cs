using System.Security.Cryptography;
using JasperFx;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// Host real de Subscription.Api (SQL Server + Redis + RabbitMQ locales, sin mocks) para los
/// tests de integración de Fase 4.10 del plan de rate limiting — mismo patrón que
/// <c>TaxVision.Correspondence.Tests.Integration.CorrespondenceApiFactory</c> (Fase 4.9). La única
/// sustitución respecto al Program.cs real es la clave de firma JWT: appsettings.Development.json
/// apunta a la clave RS256 real de Auth — en vez de replicarla, se sobreescribe la config con un
/// secreto HS256 generado en memoria SOLO para este proceso de test, nunca leído de ningún
/// user-secret real ni escrito a disco.
/// </summary>
public sealed class SubscriptionApiFactory : WebApplicationFactory<Program>
{
    public string JwtSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public string JwtIssuer => "TaxVision.Auth";
    public string JwtAudience => "TaxVision.Services";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(
            (_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Jwt:PublicKeyPath"] = string.Empty,
                        ["Jwt:PublicKeyPem"] = string.Empty,
                        ["Jwt:Secret"] = JwtSecret,
                    }
                )
        );

        // Sin Rabbit, sin los sobres de la flota y sin jobs: no compite con el Subscription en marcha (BB-10).
        builder.ConfigureTestServices(services =>
        {
            services.DisableAllExternalWolverineTransports();
            services.ConfigureWolverine(options =>
            {
                options.Durability.Mode = DurabilityMode.MediatorOnly;
                options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
            });
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                if (hosted.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal) == true)
                    services.Remove(hosted);
            }
        });
    }
}
