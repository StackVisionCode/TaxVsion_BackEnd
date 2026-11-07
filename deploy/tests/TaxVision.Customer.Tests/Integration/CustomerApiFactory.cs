using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// Host real de Customer.Api (SQL Server + Redis + RabbitMQ locales, sin mocks) para los tests de
/// integración de Fase 3 del plan de rate limiting. La única sustitución respecto al Program.cs
/// real es la clave de firma JWT: appsettings.Development.json apunta a
/// <c>dev-keys/jwt-public.pem</c> (RS256, clave real de Auth) — en vez de replicar el signing RS256
/// de Auth acá, se sobreescribe la config con un secreto HS256 generado en memoria SOLO para este
/// proceso de test, nunca leído de ningún user-secret real ni escrito a disco.
/// </summary>
public sealed class CustomerApiFactory : WebApplicationFactory<Program>
{
    public string JwtSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public string JwtIssuer => "TaxVision.Auth";
    public string JwtAudience => "TaxVision.Services";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // BB-10: base temporal; con la local compartiría los sobres de la flota.
        builder.UseSetting("ConnectionStrings:Default", CustomerIntegrationDatabase.ConnectionString);
        builder.ConfigureAppConfiguration(
            (_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Jwt:PublicKeyPath"] = string.Empty,
                        ["Jwt:PublicKeyPem"] = string.Empty,
                        ["Jwt:Secret"] = JwtSecret,
                        // Corre contra la base real: que el test no reconcilie el directorio de empleados.
                        ["EmployeeDirectoryReconciliation:Enabled"] = "false",
                    }
                )
        );
        // Sin transportes externos el host de prueba no le roba mensajes a la cola del Customer local, y sin
        // jobs propios no corre contra la base de desarrollo. MediatorOnly no sirve: los eventos de dominio van
        // a una cola local durable y cada alta fallaría.
        builder.ConfigureTestServices(services =>
        {
            services.DisableAllExternalWolverineTransports();
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                if (hosted.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal) == true)
                    services.Remove(hosted);
            }
        });
    }
}
