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
