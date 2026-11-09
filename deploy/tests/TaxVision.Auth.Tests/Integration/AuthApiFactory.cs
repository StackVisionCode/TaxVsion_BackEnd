using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Host real de Auth.Api (SQL Server + Redis locales, sin transportes externos ni jobs) para los tests de
/// integración de Fase 4.12 del plan de rate limiting — mismo patrón que
/// <c>TaxVision.Billing.Tests.Integration.BillingApiFactory</c> (Fase 4.11). La única
/// sustitución respecto al Program.cs real es la clave de firma JWT: appsettings.Development.json
/// apunta a las claves RS256 reales de dev-keys/ — en vez de replicarlas, se sobreescribe la config
/// con un secreto HS256 generado en memoria SOLO para este proceso de test, nunca leído de ningún
/// user-secret real ni escrito a disco.
/// </summary>
public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public string JwtSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public string JwtIssuer => "TaxVision.Auth";
    public string JwtAudience => "TaxVision.Services";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // BB-10: base temporal; con la local compartiría los sobres de la flota.
        builder.UseSetting("ConnectionStrings:Default", AuthIntegrationDatabase.ConnectionString);
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

        // Sin transportes externos el host de prueba no le roba mensajes a la cola del Auth local; los jobs
        // (bootstrap de PlatformAdmin, backfills, onboarding, retiros) tocan la base y otros servicios: fuera.
        builder.ConfigureTestServices(services =>
        {
            services.DisableAllExternalWolverineTransports();
            foreach (
                var job in services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(IHostedService)
                        && descriptor.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal)
                            == true
                    )
                    .ToList()
            )
                services.Remove(job);
        });
    }
}
