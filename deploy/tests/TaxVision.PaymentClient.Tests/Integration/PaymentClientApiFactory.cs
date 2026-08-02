using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>
/// Host real de PaymentClient.Api (SQL Server + Redis + RabbitMQ locales, sin mocks) para los
/// tests de integración de Fase 4.14 del plan de rate limiting — mismo patrón que
/// <c>TaxVision.PaymentApp.Tests.Integration.PaymentAppApiFactory</c> (Fase 4.13) y
/// <c>TaxVision.Auth.Tests.Integration.AuthApiFactory</c> (Fase 4.12). La única sustitución
/// respecto al Program.cs real es la clave de firma JWT: appsettings.Development.json apunta a la
/// clave RS256 real de dev-keys/ — en vez de replicarla, se sobreescribe la config con un secreto
/// HS256 generado en memoria SOLO para este proceso de test, nunca leído de ningún user-secret
/// real ni escrito a disco.
/// </summary>
public sealed class PaymentClientApiFactory : WebApplicationFactory<Program>
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
