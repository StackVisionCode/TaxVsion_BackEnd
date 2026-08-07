using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
