using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// El dominio de la plataforma vive en DOS secrets: <c>TAXVISION_BASE_DOMAIN</c> (apex) y
/// <c>TAXVISION_DOMAIN</c> (host de la API). Cuando pasamos de taxproffice.com a taxoffix.io había
/// copias sueltas (literales en Caddy y en el compose, secrets con URLs completas, el BaseDomain del
/// Gateway en appsettings) y cada una se quedaba con el dominio viejo. Estos tests fijan que todo host
/// público se derive de esas dos variables.
/// </summary>
public sealed class PlatformDomainSingleSourceDeployTests
{
    // Un host escrito a mano (ej. "app.taxoffix.io"). Sensible a mayúsculas a propósito: no confunde
    // placeholders como {env.CLOUDFLARE_API_TOKEN} ni extensiones sueltas como *.js.
    private static readonly Regex LiteralHost = new(
        @"\b[a-z0-9-]+\.[a-z0-9-]+\.[a-z]{2,}\b|\b[a-z0-9-]{3,}\.(com|io|net|org)\b"
    );

    [Fact]
    public void Caddyfile_has_no_literal_hosts_outside_the_domain_variables()
    {
        var caddyfile = ReadRepoFile("deploy/docker/caddy/Caddyfile");

        var code = string.Join(
            '\n',
            caddyfile.Replace("\r\n", "\n").Split('\n').Where(line => !line.TrimStart().StartsWith('#'))
        );
        var withoutPlaceholders = Regex.Replace(code, @"\{\$[A-Z_]+(:[^}]*)?\}", "{VAR}");

        var literal = LiteralHost.Match(withoutPlaceholders);
        Assert.False(literal.Success, $"Caddyfile hardcodes the host '{literal.Value}'.");
    }

    [Theory]
    [InlineData("COMMUNICATION_FRONTEND_BASE_URL: https://client.${TAXVISION_BASE_DOMAIN:-taxoffix.io}")]
    [InlineData("PlatformSettings__TenantBaseUrl: http://tenant-api:8080")]
    [InlineData("PlatformSettings__BaseDomain: ${TAXVISION_BASE_DOMAIN:-taxoffix.io}")]
    [InlineData("PlatformSettings__ApiHost: ${TAXVISION_DOMAIN:-api.taxoffix.io}")]
    [InlineData("COMMUNICATION_TENANT_BASE_URL: http://tenant-api:8080")]
    [InlineData("COMMUNICATION_PLATFORM_BASE_DOMAIN: ${TAXVISION_BASE_DOMAIN:-taxoffix.io}")]
    public void Compose_derives_public_hosts_from_the_domain_variables(string line)
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Contains(line, compose);
    }

    /// <summary>
    /// El dominio de las oficinas y los orígenes CORS de las apps los decide PlatformSettings. Una copia
    /// propia por servicio es justo lo que se quedaba atrás al cambiar de dominio.
    /// </summary>
    [Theory]
    [InlineData("TenantHostGuard__BaseDomain")]
    [InlineData("TenantDomains__BaseDomain")]
    [InlineData("TenantDomains__PlatformHost")]
    [InlineData("Onboarding__TenantBaseDomain")]
    [InlineData("Cors__Origins__")]
    [InlineData("AccountSession__AllowedOrigins__")]
    public void Compose_has_no_per_service_copy_of_the_platform_domain(string key)
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.DoesNotContain(key, compose);
    }

    [Fact]
    public void Compose_has_no_literal_platform_hosts()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        var code = string.Join(
            '\n',
            compose.Replace("\r\n", "\n").Split('\n').Where(line => !line.TrimStart().StartsWith('#'))
        );
        // Lo que está dentro de ${VAR:-default} es el default de un placeholder, no un literal.
        var withoutPlaceholders = Regex.Replace(code, @"\$\{[A-Z0-9_]+(:?-[^}]*)?\}", "{VAR}");

        foreach (Match url in Regex.Matches(withoutPlaceholders, @"https://(?<host>[^\s,/""']+)"))
        {
            var host = url.Groups["host"].Value;
            Assert.True(
                host.Contains("{VAR}", StringComparison.Ordinal) || IsThirdParty(host),
                $"docker-compose.yml hardcodes the public host '{host}'."
            );
        }
    }

    [Theory]
    [InlineData("PORTAL_BASE_URL=https://app.${{ secrets.TAXVISION_BASE_DOMAIN }}")]
    [InlineData("CLIENT_PORTAL_BASE_URL=https://client.${{ secrets.TAXVISION_BASE_DOMAIN }}")]
    [InlineData("MINIO_PUBLIC_ENDPOINT=${{ secrets.TAXVISION_DOMAIN }}")]
    [InlineData("PAYMENTCLIENT_PUBLIC_BASE_URL=https://${{ secrets.TAXVISION_DOMAIN }}")]
    [InlineData("PAYMENTCLIENT_CHECKOUT_PAGE_BASE_URL=https://app.${{ secrets.TAXVISION_BASE_DOMAIN }}")]
    [InlineData(
        "COMMUNICATION_TURN_URL=turn:turn.${{ secrets.TAXVISION_BASE_DOMAIN }}:3478,"
            + "turns:turn.${{ secrets.TAXVISION_BASE_DOMAIN }}:5349?transport=tcp"
    )]
    public void Deploy_workflow_derives_public_urls_from_the_domain_secrets(string line)
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        Assert.Contains(line, workflow);
    }

    /// <summary>
    /// Secrets que llevaban un host o una URL completa del dominio. Ya no los lee el deploy: si alguien
    /// vuelve a cablear uno, el próximo cambio de dominio lo deja atrás (pasó con el TURN).
    /// </summary>
    [Theory]
    [InlineData("COMMUNICATION_TURN_URL")]
    [InlineData("COMMUNICATION_CORS_ORIGINS")]
    [InlineData("COMMUNICATION_FRONTEND_BASE_URL")]
    [InlineData("PORTAL_BASE_URL")]
    [InlineData("CLIENT_PORTAL_BASE_URL")]
    [InlineData("PAYMENTCLIENT_PUBLIC_BASE_URL")]
    [InlineData("PAYMENTCLIENT_CHECKOUT_PAGE_BASE_URL")]
    [InlineData("PAYMENTCLIENT_PUBLIC_TENANT_BASE_DOMAIN")]
    [InlineData("MINIO_PUBLIC_ENDPOINT")]
    public void Deploy_workflow_does_not_read_domain_bearing_secrets(string secret)
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        Assert.DoesNotContain($"secrets.{secret} }}}}", workflow);
    }

    [Fact]
    public void Deploy_workflow_fails_fast_without_the_domain_secrets()
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        var validate = workflow.IndexOf("name: Validate domain secrets", StringComparison.Ordinal);
        var writeEnv = workflow.IndexOf("name: Write production .env (1/3)", StringComparison.Ordinal);
        Assert.True(validate >= 0 && validate < writeEnv, "The domain secrets are not validated before writing .env.");
    }

    // Hosts de proveedores, no de la plataforma.
    private static bool IsThirdParty(string host) =>
        host.EndsWith("smtp2go.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("paypal.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("freetsa.org", StringComparison.OrdinalIgnoreCase)
        || host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase);

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' walking up from {AppContext.BaseDirectory}."
        );
    }
}
