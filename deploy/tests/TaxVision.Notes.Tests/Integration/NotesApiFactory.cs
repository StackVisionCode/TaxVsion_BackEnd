using System.Security.Cryptography;
using BuildingBlocks.CustomerVisibility;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TaxVision.Notes.Infrastructure.Jobs;
using Wolverine;

namespace TaxVision.Notes.Tests.Integration;

/// <summary>
/// Host real de Notes.Api (SQL Server + Redis + RabbitMQ locales, sin mocks) para el test de
/// Fase 10 (03_Plan_De_Fases.md §Fase 10, guardrail RateLimit #10) — mismo patrón que
/// <c>TaxVision.Correspondence.Tests.Integration.CorrespondenceApiFactory</c>. La única sustitución
/// respecto al Program.cs real es la clave de firma JWT: appsettings.Development.json apunta a la
/// clave RS256 real de Auth — en vez de replicarla, se sobreescribe la config con un secreto HS256
/// generado en memoria SOLO para este proceso de test, nunca leído de ningún user-secret real ni
/// escrito a disco.
/// </summary>
public sealed class NotesApiFactory : WebApplicationFactory<Program>
{
    public string JwtSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public string JwtIssuer => "TaxVision.Auth";
    public string JwtAudience => "TaxVision.Services";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // BB-10: base temporal; con la local compartiría los sobres de la flota.
        builder.UseSetting("ConnectionStrings:Default", NotesIntegrationDatabase.ConnectionString);
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

        // Los jobs llaman a Customer y CloudStorage reales con datos de la base local: no en pruebas.
        builder.ConfigureTestServices(services =>
        {
            // Sin Rabbit: la cola notes-events es la de la flota y este host le quitaría mensajes.
            services.DisableAllExternalWolverineTransports();

            Type[] jobs =
            [
                typeof(CustomerDirectoryReconciliationJob),
                typeof(TenantCustomerFullReconciliationJob),
                typeof(PendingAttachmentReconciliationJob),
                typeof(CustomerAssignmentReconciliationJob),
            ];
            foreach (
                var descriptor in services
                    .Where(d => d.ServiceType == typeof(IHostedService) && jobs.Contains(d.ImplementationType))
                    .ToList()
            )
                services.Remove(descriptor);
        });
    }
}
