namespace TaxVision.Auth.Tests.Deploy;

public sealed class AuthOnboardingDeployConfigWiringTests
{
    [Fact]
    public void Auth_onboarding_uses_container_dns_for_internal_m2m_calls()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Contains("Auth__Growth__BaseUrl: http://growth-api:8080", compose);
        Assert.Contains("Auth__Subscription__BaseUrl: http://subscription-api:8080", compose);
        Assert.Contains("Auth__PaymentApp__BaseUrl: http://payment-app-api:8080", compose);
        Assert.Contains("Auth__Tenant__BaseUrl: http://tenant-api:8080", compose);
        Assert.Contains("Auth__Documents__BaseUrl: http://documents-api:8080", compose);
        Assert.Contains("Auth__CloudStorage__BaseUrl: http://cloudstorage-api:8080", compose);
    }

    [Fact]
    public void Auth_onboarding_separates_internal_loopback_from_public_user_urls()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Contains("Onboarding__AuthPublicBaseUrl: http://auth-api:8080", compose);
        Assert.Contains(
            "Onboarding__ReceiptDownloadBaseUrl: https://${TAXVISION_DOMAIN:-api.taxproffice.com}",
            compose
        );
        Assert.Contains("Onboarding__RegistrationUrlBase: https://${TAXVISION_BASE_DOMAIN:-taxproffice.com}", compose);
        Assert.Contains("Onboarding__TenantBaseDomain: ${TAXVISION_BASE_DOMAIN:-taxproffice.com}", compose);
    }

    [Fact]
    public void Deploy_workflow_exports_domains_consumed_by_onboarding()
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        Assert.Contains("TAXVISION_DOMAIN=${{ secrets.TAXVISION_DOMAIN }}", workflow);
        Assert.Contains("TAXVISION_BASE_DOMAIN=${{ secrets.TAXVISION_BASE_DOMAIN }}", workflow);
    }

    [Fact]
    public void Local_auth_onboarding_registration_url_points_to_landing_frontend()
    {
        var appsettings = ReadRepoFile("src/Services/Auth/Api/appsettings.json");

        Assert.Contains("\"RegistrationUrlBase\": \"http://localhost:4200\"", appsettings);
        Assert.DoesNotContain("\"RegistrationUrlBase\": \"http://localhost:5173\"", appsettings);
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
