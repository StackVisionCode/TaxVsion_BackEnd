using System.Text.RegularExpressions;

namespace TaxVision.Auth.Tests.SlotGating;

/// <summary>
/// Garantiza que SOLO el Gateway sea entry point público. Si falla, el middleware de slot gating
/// no cubre tráfico directo a un servicio — el request esquivaría el 403 llegando por otro port.
/// </summary>
public sealed class DockerComposeNoPublicApiPortsFitnessTests
{
    [Fact]
    public void No_api_service_publishes_ports_to_the_host()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        var offenders = ApiServiceBlocks(compose)
            .Where(block => HasActivePortsSection(block.body))
            .Select(block => block.name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Estos -api/-service exponen `ports:` al host — el único entry point público debe ser gateway: "
                + string.Join(", ", offenders)
        );
    }

    [Fact]
    public void Gateway_remains_the_only_entry_point_with_published_ports()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        var gatewayBlock = ServiceBlocks(compose).Single(block => block.name == "gateway");
        Assert.True(
            HasActivePortsSection(gatewayBlock.body),
            "El servicio gateway debe seguir publicando ports: — es el único entry point público."
        );
    }

    private static IEnumerable<(string name, string body)> ApiServiceBlocks(string compose) =>
        ServiceBlocks(compose).Where(block => block.name.EndsWith("-api", StringComparison.Ordinal));

    private static IEnumerable<(string name, string body)> ServiceBlocks(string compose)
    {
        // Cabeceras de servicio: 2 espacios + nombre + ":"; secciones (ports/expose) van con ≥4 espacios.
        var header = new Regex("^  (?<name>[a-z][a-z0-9-]*):\\s*$", RegexOptions.Multiline);
        var matches = header.Matches(compose);
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : compose.Length;
            yield return (matches[i].Groups["name"].Value, compose[start..end]);
        }
    }

    private static bool HasActivePortsSection(string body)
    {
        // Línea `    ports:` (4 espacios) que no esté comentada. Un `# ports:` dentro de la sección
        // documenta un servicio con ports deshabilitados y no cuenta.
        var portsHeader = new Regex("^    ports:\\s*$", RegexOptions.Multiline);
        var match = portsHeader.Match(body);
        if (!match.Success)
            return false;

        var after = body[(match.Index + match.Length)..];
        var firstEntry = new Regex("^      -\\s", RegexOptions.Multiline).Match(after);
        if (!firstEntry.Success)
            return false;
        // Si entre el header y la primera entrada hay otra sección (otra clave con 4 espacios),
        // asumimos ports: vacío/colapsado — pero el formato YAML real no permite listas vacías así,
        // así que si aparece una entrada `- ...` después del header, es un ports: activo.
        return true;
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
