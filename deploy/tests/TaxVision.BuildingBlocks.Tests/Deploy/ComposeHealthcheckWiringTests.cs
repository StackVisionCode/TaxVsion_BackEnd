using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Guarda del deploy roto del 2026-10-03: <c>scribe-api</c> declaraba
/// <c>tenant-api: condition: service_healthy</c> y <c>tenant-api</c> no tenía <c>healthcheck</c>.
/// Docker aborta el despliegue entero con "container X has no healthcheck configured" — no degrada,
/// no arranca nada.
///
/// <para>Y la otra mitad de la trampa: el healthcheck usa <c>curl</c>, que la imagen de ASP.NET no
/// trae. Un healthcheck sin curl en el Dockerfile deja el contenedor <c>unhealthy</c> para siempre y
/// bloquea a todo el que lo espere — igual de fatal, pero más difícil de leer en los logs.</para>
///
/// <para>Las dos mitades se revisan acá porque las dos se arreglan con un cuidado que no escala: este
/// error se cometió dos veces seguidas en el mismo cambio.</para>
/// </summary>
public sealed class ComposeHealthcheckWiringTests
{
    [Fact]
    public void Every_service_healthy_dependency_targets_a_service_that_defines_one()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        var withHealthcheck = ServicesDefiningAHealthcheck(compose);

        var offenders = Regex
            .Matches(
                compose,
                @"^      (?<target>[a-z0-9-]+):\r?\n\s+condition:\s*service_healthy",
                RegexOptions.Multiline
            )
            .Select(m => m.Groups["target"].Value)
            .Distinct()
            .Where(target => !withHealthcheck.Contains(target))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Estos servicios se esperan con service_healthy pero no declaran healthcheck "
                + "(docker aborta el deploy entero): "
                + string.Join(", ", offenders)
        );
    }

    [Fact]
    public void Every_curl_healthcheck_runs_on_an_image_that_installs_curl()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        var offenders = new List<string>();

        foreach (var service in ServicesDefiningAHealthcheck(compose))
        {
            var block = ServiceBlock(compose, service);
            if (!block.Contains("curl", StringComparison.Ordinal))
                continue;

            var dockerfile = Regex.Match(block, @"dockerfile:\s*(?<path>\S+)").Groups["path"].Value;
            if (dockerfile.Length == 0)
                continue; // Imagen de terceros (redis, minio…): su propia imagen ya trae lo que use.

            if (!ReadRepoFile(dockerfile).Contains("curl", StringComparison.Ordinal))
                offenders.Add($"{service} ({dockerfile})");
        }

        Assert.True(
            offenders.Count == 0,
            "Healthcheck con curl sobre una imagen que no lo instala — el contenedor queda unhealthy "
                + "para siempre: "
                + string.Join(", ", offenders)
        );
    }

    private static HashSet<string> ServicesDefiningAHealthcheck(string compose)
    {
        var services = new HashSet<string>(StringComparer.Ordinal);
        string? current = null;
        foreach (var line in compose.Split('\n'))
        {
            var service = Regex.Match(line, @"^  (?<name>[a-z0-9-]+):\s*$");
            if (service.Success)
                current = service.Groups["name"].Value;
            else if (current is not null && Regex.IsMatch(line, @"^    healthcheck:\s*$"))
                services.Add(current);
        }

        return services;
    }

    private static string ServiceBlock(string compose, string service)
    {
        var lines = compose.Split('\n');
        var start = Array.FindIndex(lines, l => Regex.IsMatch(l, $@"^  {Regex.Escape(service)}:\s*$"));
        if (start < 0)
            return string.Empty;

        var end = Array.FindIndex(lines, start + 1, l => Regex.IsMatch(l, @"^  [a-z0-9-]+:\s*$"));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
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
