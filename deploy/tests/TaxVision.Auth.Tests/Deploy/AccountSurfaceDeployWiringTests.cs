namespace TaxVision.Auth.Tests.Deploy;

public sealed class AccountSurfaceDeployWiringTests
{
    [Fact]
    public void Production_never_turns_host_resolution_off()
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot(), "deploy", "docker", "docker-compose.yml"));

        Assert.DoesNotContain("TenantDomains__EnforceHostResolution", compose);
    }

    [Fact]
    public void Production_declares_the_landing_origins_for_the_account_cookie()
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot(), "deploy", "docker", "docker-compose.yml"));

        Assert.Contains(
            "AccountSession__AllowedOrigins__0: https://${TAXVISION_BASE_DOMAIN:-taxproffice.com}",
            compose
        );
        Assert.Contains(
            "AccountSession__AllowedOrigins__1: https://www.${TAXVISION_BASE_DOMAIN:-taxproffice.com}",
            compose
        );
    }

    [Fact]
    public void Only_the_services_the_account_uses_accept_its_audience()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var accepting = Directory
            .EnumerateFiles(src, "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("\"TaxVision.Account\"", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(Path.GetDirectoryName(path))!)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Api",
                "TaxVision.Gateway",
                "TaxVision.PaymentApp.Api",
                "TaxVision.Subscription.Api",
                "TaxVision.Tenant.Api",
            },
            accepting
        );
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj" or "node_modules");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
