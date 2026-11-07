using JasperFx;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// Host real de Growth.Api (SQL Server + Redis + RabbitMQ locales, sin mocks) para los tests de
/// integración de Fase 4.15 del plan de rate limiting — mismo patrón que
/// <c>TaxVision.PaymentClient.Tests.Integration.PaymentClientApiFactory</c> (Fase 4.14). La única
/// sustitución respecto al Program.cs real es la clave de firma JWT: se sobreescribe con un
/// secreto HS256 generado en memoria SOLO para este proceso de test, nunca leído de ningún
/// user-secret real ni escrito a disco.
/// </summary>
public sealed class GrowthApiFactory : WebApplicationFactory<Program>
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
        // Sin transportes externos el host de prueba no le roba mensajes a la cola del Growth local; el barrido de
        // reservas tampoco corre contra la base de desarrollo.
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
                        && descriptor.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal)
                            == true
                    )
                    .ToList()
            )
                services.Remove(job);
        });
    }
}
