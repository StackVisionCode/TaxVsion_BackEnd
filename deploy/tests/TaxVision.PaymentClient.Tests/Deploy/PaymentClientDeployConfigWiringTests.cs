using Xunit;

namespace TaxVision.PaymentClient.Tests.Deploy;

public sealed class PaymentClientDeployConfigWiringTests
{
    /// <summary>
    /// Producción compone la URL de la factura en el host de la oficina; el dominio sale de
    /// PlatformSettings, así que no hay un secret propio con el dominio que se quede atrás.
    /// </summary>
    [Fact]
    public void Tenant_hosts_are_on_in_compose_without_a_domain_secret_of_its_own()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        Assert.Contains("PaymentClient__Public__UseTenantHosts: \"true\"", compose);
        Assert.DoesNotContain("PaymentClient__Public__TenantBaseDomain", compose);

        var workflow = ReadRepoFile(".github/workflows/deploy.yml");
        Assert.DoesNotContain("PAYMENTCLIENT_PUBLIC_TENANT_BASE_DOMAIN", workflow);
    }

    /// <summary>
    /// El registro automático del webhook está encendido en producción (decisión del dueño): su base es el
    /// mismo host público https de la API que PaymentClient ya usa, por donde el proveedor llega a los webhooks.
    /// </summary>
    [Fact]
    public void Webhook_auto_setup_base_url_points_to_the_public_api_host()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        Assert.Contains("PaymentClient__Webhooks__PublicBaseUrl: ${PAYMENTCLIENT_WEBHOOKS_PUBLIC_BASE_URL:-}", compose);

        var workflow = ReadRepoFile(".github/workflows/deploy.yml");
        var publicBase = Assert.Single(ValuesOf(workflow, "PAYMENTCLIENT_PUBLIC_BASE_URL"));
        var webhookBase = Assert.Single(ValuesOf(workflow, "PAYMENTCLIENT_WEBHOOKS_PUBLIC_BASE_URL"));
        Assert.StartsWith("https://", webhookBase);
        Assert.Equal(publicBase, webhookBase);
    }

    private static IEnumerable<string> ValuesOf(string workflow, string variable) =>
        System
            .Text.RegularExpressions.Regex.Matches(workflow, $@"(?m)^\s+{variable}=(.*?)\r?$")
            .Select(m => m.Groups[1].Value.Trim());

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
