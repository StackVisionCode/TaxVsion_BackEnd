using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Todo <c>*_CLIENT_SECRET</c> que el compose lee tiene que escribirlo <c>deploy.yml</c> en el .env de
/// producción. Si falta, el compose cae a su default vacío y el servicio arranca sin credencial M2M:
/// cada llamada a Auth falla en silencio (pasó con Calendar: el portal mostraba "Your preparer" sin
/// nombre y no resolvía el dominio de la oficina).
/// </summary>
public sealed class ServiceClientSecretsDeployWiringTests
{
    [Fact]
    public void Every_client_secret_the_compose_reads_is_written_by_the_deploy_workflow()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        var secrets = Regex
            .Matches(compose, @"\$\{(?<name>[A-Z0-9_]+_CLIENT_SECRET)[:}]")
            .Select(match => match.Groups["name"].Value)
            .Distinct()
            .ToList();
        var missing = secrets.Where(name =>
            !workflow.Contains($"{name}=${{{{ secrets.{name} }}}}", StringComparison.Ordinal)
        );

        Assert.NotEmpty(secrets);
        Assert.Empty(missing);
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
