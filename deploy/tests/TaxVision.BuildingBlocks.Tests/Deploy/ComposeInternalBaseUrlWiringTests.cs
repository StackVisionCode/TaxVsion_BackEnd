using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Guarda de regresión del incidente de prod 2026-09-16: auth-api y tenant-api no tenían
/// <c>SubscriptionClient__BaseUrl</c> y notification-api no tenía <c>Notification__Customer__BaseUrl</c>,
/// así que caían al default de dev <c>http://localhost:PORT</c> → Connection refused en el contenedor
/// (las cuotas por tier no cargaban y la reconciliación de clientes fallaba). En la red de Docker
/// TODA base-URL interna debe apuntar al DNS del servicio (<c>http://&lt;svc&gt;-api:8080</c>), nunca a
/// localhost. Estos tests fallan en CI si alguien vuelve a omitir un override.
/// </summary>
public sealed class ComposeInternalBaseUrlWiringTests
{
    [Fact]
    public void No_internal_base_url_env_points_to_localhost_in_compose()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        // Cualquier env que termine en __BaseUrl y valga http://localhost:PORT es un cliente interno
        // sin override — en el contenedor eso es siempre Connection refused. Los BaseUrl públicos usan
        // https://${...} o ${VAR:-...}, no un http://localhost literal, así que no dan falso positivo.
        var offenders = Regex
            .Matches(compose, @"^\s*[A-Za-z0-9_]+__BaseUrl:\s*http://localhost:\d+\s*$", RegexOptions.Multiline)
            .Select(m => m.Value.Trim())
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Base-URL(s) internas apuntando a localhost en docker-compose (deben usar el DNS del servicio):\n"
                + string.Join("\n", offenders)
        );
    }

    [Fact]
    public void Services_set_internal_base_urls_to_container_dns()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        // (servicio, env, valor esperado) — cada uno fue una omisión real en el incidente de prod.
        var expectations = new[]
        {
            ("auth-api", "SubscriptionClient__BaseUrl", "http://subscription-api:8080"),
            ("tenant-api", "SubscriptionClient__BaseUrl", "http://subscription-api:8080"),
            ("notification-api", "Notification__Customer__BaseUrl", "http://customer-api:8080"),
        };

        var missing = new List<string>();
        foreach (var (service, key, expected) in expectations)
        {
            var block = ExtractServiceBlock(compose, service);
            if (!block.Contains($"{key}: {expected}"))
                missing.Add($"{service} → {key}: {expected}");
        }

        Assert.True(
            missing.Count == 0,
            "Overrides de base-URL interna faltantes en compose:\n" + string.Join("\n", missing)
        );
    }

    // Recorta el bloque de un servicio (desde "  <name>:" hasta el siguiente servicio al mismo nivel).
    private static string ExtractServiceBlock(string compose, string service)
    {
        var lines = compose.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l == $"  {service}:");
        Assert.True(start >= 0, $"No se encontró el bloque del servicio '{service}' en docker-compose.yml.");

        var end = lines.Length;
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], @"^  [A-Za-z0-9_-]+:\s*$"))
            {
                end = i;
                break;
            }
        }

        return string.Join("\n", lines[start..end]);
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
