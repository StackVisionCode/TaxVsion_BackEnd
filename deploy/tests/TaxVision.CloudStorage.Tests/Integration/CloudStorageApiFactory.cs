using JasperFx;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// Host real de CloudStorage.Api (SQL Server + Redis + RabbitMQ locales, sin mocks) para los
/// tests de integración de Fase 4.6 del plan de rate limiting — mismo patrón que
/// <c>TaxVision.Scribe.Tests.Integration.ScribeApiFactory</c> (Fase 4.5). La única sustitución
/// respecto al Program.cs real es la clave de firma JWT: appsettings.Development.json apunta a
/// <c>dev-keys/jwt-public.pem</c> (RS256, clave real de Auth) — en vez de replicar el signing
/// RS256 de Auth acá, se sobreescribe la config con un secreto HS256 generado en memoria SOLO
/// para este proceso de test, nunca leído de ningún user-secret real ni escrito a disco.
/// </summary>
public sealed class CloudStorageApiFactory : WebApplicationFactory<Program>
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
                        // El host de prueba comparte la base local: no debe rescatar archivos reales.
                        ["CloudStorage:StuckScanRescueEnabled"] = "false",
                    }
                )
        );

        // Sin transportes externos el host de prueba no le roba mensajes a la cola del servicio local;
        // los jobs y seeders corren contra la base local y llaman a servicios reales: fuera en pruebas.
        builder.ConfigureTestServices(services =>
        {
            services.DisableAllExternalWolverineTransports();
            // Sin los sobres de la flota: MediatorOnly conserva el almacén sin migrarlo ni leerlo (BB-10).
            services.ConfigureWolverine(options =>
            {
                options.Durability.Mode = DurabilityMode.MediatorOnly;
                options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
            });
            foreach (
                var job in services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(IHostedService)
                        && (
                            descriptor.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal)
                                == true
                            || descriptor.ImplementationType?.Namespace?.StartsWith(
                                "BuildingBlocks.",
                                StringComparison.Ordinal
                            ) == true
                        )
                    )
                    .ToList()
            )
                services.Remove(job);
        });
    }
}
