namespace TaxVision.Auth.Tests.Deploy;

public sealed class AccountSurfaceDeployWiringTests
{
    [Fact]
    public void Production_never_turns_host_resolution_off()
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot(), "deploy", "docker", "docker-compose.yml"));

        Assert.DoesNotContain("TenantDomains__EnforceHostResolution", compose);
    }

    /// <summary>
    /// Los orígenes del landing salen del dominio de PlatformSettings: una lista fija en el compose es justo
    /// lo que se quedaba con el dominio viejo al cambiarlo.
    /// </summary>
    [Fact]
    public void Production_does_not_pin_the_landing_origins_for_the_account_cookie()
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot(), "deploy", "docker", "docker-compose.yml"));

        Assert.DoesNotContain("AccountSession__AllowedOrigins", compose);
    }

    /// <summary>
    /// El localhost del landing local es solo de desarrollo. Antes el compose pisaba ese índice del array
    /// con el dominio real; al sacar la lista del compose, si quedara en appsettings.json producción aceptaría
    /// <c>http://localhost:4201</c> en el chequeo anti-CSRF del Account.
    /// </summary>
    [Fact]
    public void The_local_landing_origin_is_only_allowed_in_development()
    {
        var api = Path.Combine(RepoRoot(), "src", "Services", "Auth", "Api");

        Assert.Equal([], AccountSessionOrigins(Path.Combine(api, "appsettings.json")));
        Assert.Contains(
            "http://localhost:4201",
            AccountSessionOrigins(Path.Combine(api, "appsettings.Development.json"))
        );
    }

    private static string[] AccountSessionOrigins(string appsettingsPath)
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        return
        [
            .. json
                .RootElement.GetProperty("AccountSession")
                .GetProperty("AllowedOrigins")
                .EnumerateArray()
                .Select(e => e.GetString()!),
        ];
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
