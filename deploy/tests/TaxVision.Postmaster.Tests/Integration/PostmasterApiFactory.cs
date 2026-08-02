using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>
/// Host real de Postmaster.Api (SQL Server + Redis + RabbitMQ locales, sin mocks) para los tests
/// de integración de Fase 4.4 del plan de rate limiting — mismo patrón que
/// <c>TaxVision.Tenant.Tests.Integration.TenantApiFactory</c> (Fase 4.2) y
/// <c>TaxVision.Notification.Tests.Integration.NotificationApiFactory</c> (Fase 4.3). La única
/// sustitución respecto al Program.cs real es la clave de firma JWT: appsettings.Development.json
/// apunta a <c>dev-keys/jwt-public.pem</c> (RS256, clave real de Auth) — en vez de replicar el
/// signing RS256 de Auth acá, se sobreescribe la config con un secreto HS256 generado en memoria
/// SOLO para este proceso de test, nunca leído de ningún user-secret real ni escrito a disco.
/// </summary>
public sealed class PostmasterApiFactory : WebApplicationFactory<Program>
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
    }
}
