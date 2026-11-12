using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaxVision.Subscription.Tests.Api;

/// <summary>
/// D7: las oficinas nuevas arrancan su trial en Enterprise, el único plan a la venta. El compose de producción pisa
/// a appsettings, así que los dos tienen que decir lo mismo; si no, R2 se niega a correr y el servicio no arranca.
/// </summary>
public sealed class TrialPlanDeployWiringTests
{
    [Fact]
    public void Appsettings_and_the_production_compose_start_the_trial_on_enterprise()
    {
        var settings = JsonDocument.Parse(
            ReadRepoFile("src/Services/Subscription/TaxVision.Subscription.Api/appsettings.json")
        );
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Equal(
            "enterprise",
            settings.RootElement.GetProperty("Subscriptions").GetProperty("DefaultPlanCode").GetString()
        );
        var values = Regex
            .Matches(compose, @"Subscriptions__DefaultPlanCode:\s*(?<value>\S+)")
            .Select(match => match.Groups["value"].Value)
            .ToList();
        Assert.NotEmpty(values);
        Assert.All(values, value => Assert.Equal("enterprise", value));
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
