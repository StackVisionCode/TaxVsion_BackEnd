using Xunit;

namespace TaxVision.PaymentClient.Tests.Deploy;

public sealed class PaymentClientDeployConfigWiringTests
{
    [Fact]
    public void Tenant_base_domain_is_wired_in_compose_and_deploy_workflow()
    {
        const string varName = "PAYMENTCLIENT_PUBLIC_TENANT_BASE_DOMAIN";

        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        Assert.Contains($"PaymentClient__Public__TenantBaseDomain: ${{{varName}-}}", compose);

        var workflow = ReadRepoFile(".github/workflows/deploy.yml");
        Assert.Contains($"{varName}=${{{{ secrets.{varName} }}}}", workflow);
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
