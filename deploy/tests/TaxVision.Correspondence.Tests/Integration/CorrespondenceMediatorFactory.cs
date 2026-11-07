using System.Security.Cryptography;
using JasperFx;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TaxVision.Correspondence.Application.Abstractions;
using Wolverine;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>
/// Host real con el pipeline de Wolverine (transacción EF incluida), sin leer ni escribir sobres de la flota:
/// MediatorOnly no toca las tablas de Wolverine. Postmaster es falso: ningún correo sale.
/// </summary>
public sealed class CorrespondenceMediatorFactory : WebApplicationFactory<Program>
{
    public IdempotentPostmaster Postmaster { get; } = new();
    public FlakyAuditLogRepository AuditLogs { get; } = new();

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
                        ["Jwt:Secret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                    }
                )
        );
        builder.ConfigureTestServices(services =>
        {
            services.DisableAllExternalWolverineTransports();
            services.ConfigureWolverine(options =>
            {
                options.Durability.Mode = DurabilityMode.MediatorOnly;
                options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
            });
            foreach (
                var job in services
                    .Where(d =>
                        d.ServiceType == typeof(IHostedService)
                        && d.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal) == true
                    )
                    .ToList()
            )
                services.Remove(job);
            services.RemoveAll<IPostmasterClient>();
            services.AddSingleton<IPostmasterClient>(Postmaster);
            services.RemoveAll<ICorrespondenceAuditLogRepository>();
            services.AddSingleton<ICorrespondenceAuditLogRepository>(AuditLogs);
        });
    }
}
