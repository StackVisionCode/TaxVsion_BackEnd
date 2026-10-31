using System.Text.RegularExpressions;

namespace TaxVision.Auth.Tests.SlotGating;

/// <summary>
/// Garantiza que SOLO el Gateway sea entry point público. Si falla, el middleware de slot gating
/// no cubre tráfico directo a un servicio — el request esquivaría el 403 llegando por otro port.
/// Única excepción declarada: los puertos de MEDIA WebRTC del SFU de communication-api (UDP/TCP del
/// WebRtcServer de mediasoup). No son HTTP: no esquivan el gateway ni el slot gating, y sin publicarlos
/// ninguna reunión SFU tiene audio/video (INV-02 H-1). Se exige que esos puertos salgan de la variable
/// del SFU y nunca del puerto HTTP del servicio.
/// </summary>
public sealed class DockerComposeNoPublicApiPortsFitnessTests
{
    private const string SfuMediaService = "communication-api";
    private const string SfuPortVariable = "COMMUNICATION_MEDIASOUP_WEBRTC_SERVER_PORT_BASE";

    [Fact]
    public void No_api_service_publishes_ports_to_the_host()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        var offenders = ApiServiceBlocks(compose)
            .Where(block => HasActivePortsSection(block.body))
            .Where(block => !(block.name == SfuMediaService && PublishesOnlySfuMediaPorts(block.body)))
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
    public void Communication_api_publishes_only_sfu_media_ports()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        var block = ServiceBlocks(compose).Single(b => b.name == SfuMediaService);

        Assert.True(
            HasActivePortsSection(block.body),
            "communication-api debe publicar los puertos del WebRtcServer (UDP y TCP)."
        );
        Assert.True(
            PublishesOnlySfuMediaPorts(block.body),
            "communication-api solo puede publicar puertos de media del SFU (derivados de "
                + SfuPortVariable
                + ", con sufijo /udp o /tcp); nunca su puerto HTTP 5350."
        );
    }

    /// <summary>Cada entrada de `ports:` debe venir de la variable del SFU y llevar protocolo explícito.</summary>
    private static bool PublishesOnlySfuMediaPorts(string body)
    {
        var entries = PortEntries(body);
        return entries.Count > 0
            && entries.All(entry =>
                entry.Contains(SfuPortVariable, StringComparison.Ordinal)
                && (
                    entry.EndsWith("/udp", StringComparison.Ordinal) || entry.EndsWith("/tcp", StringComparison.Ordinal)
                )
                && !entry.Contains("5350", StringComparison.Ordinal)
            );
    }

    private static List<string> PortEntries(string body)
    {
        var portsHeader = new Regex("^    ports:\\s*$", RegexOptions.Multiline);
        var match = portsHeader.Match(body);
        if (!match.Success)
            return new List<string>();
        var after = body[(match.Index + match.Length)..];
        var entries = new List<string>();
        foreach (var line in after.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("      - ", StringComparison.Ordinal))
                entries.Add(trimmed["      - ".Length..].Trim().Trim('"'));
            else if (trimmed.Trim().Length > 0 && !trimmed.StartsWith("      ", StringComparison.Ordinal))
                break; // otra clave del servicio: fin de la sección ports
        }
        return entries;
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
