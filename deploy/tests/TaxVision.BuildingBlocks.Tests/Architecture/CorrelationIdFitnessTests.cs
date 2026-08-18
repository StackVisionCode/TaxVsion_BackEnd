using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// Todo evento de integración tiene que salir con <c>CorrelationId</c>. El campo existe en el
/// contrato base pero arranca en <c>string.Empty</c>: si el publicador se olvida, el evento sale con
/// cadena vacía, la traza se corta ahí y <b>no falla nada</b>.
///
/// <para>
/// Medido el 14-ago-2026: 24 construcciones en 8 servicios lo tenían vacío, y Catalog no lo ponía en
/// ninguna. Nada lo impedía porque nada lo comprobaba.
/// </para>
/// </summary>
public sealed class CorrelationIdFitnessTests
{
    /// <summary>Un bloque de inicialización de objeto tras <c>new XxxIntegrationEvent</c>.</summary>
    private static readonly Regex EventInitializer = new(
        @"new\s+(\w*IntegrationEvent)\s*(?:\(\s*\))?\s*\{(.*?)\n(\s*)\}",
        RegexOptions.Singleline | RegexOptions.Compiled
    );

    /// <summary><c>new()</c> con tipo inferido; sólo cuenta si el bloque rellena <c>TenantId</c>.</summary>
    private static readonly Regex InferredInitializer = new(
        @"new\(\)\s*\n?\s*\{(.*?)\n(\s*)\}",
        RegexOptions.Singleline | RegexOptions.Compiled
    );

    [Fact]
    public void Every_published_integration_event_sets_its_correlation_id()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var source = File.ReadAllText(file);
            if (!source.Contains("IntegrationEvent", StringComparison.Ordinal))
                continue;

            foreach (Match match in EventInitializer.Matches(source))
            {
                if (!match.Groups[2].Value.Contains("CorrelationId", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}: {match.Groups[1].Value}");
            }

            foreach (Match match in InferredInitializer.Matches(source))
            {
                var body = match.Groups[1].Value;
                if (!IsEventInitializer(source, match.Index, body))
                    continue;

                if (!body.Contains("CorrelationId", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}: new() con tipo inferido");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Eventos de integración publicados sin CorrelationId — la traza se corta ahí y no falla nada:\n  "
                + string.Join("\n  ", offenders.Order())
        );
    }

    /// <summary>
    /// Un <c>new()</c> con <c>TenantId</c> también lo tiene una entidad. Sólo cuenta si el método que
    /// lo devuelve declara un tipo de evento — así <c>TenantLogoRef</c> y compañía no dan un falso
    /// positivo.
    /// </summary>
    private static bool IsEventInitializer(string source, int index, string body)
    {
        if (!body.Contains("TenantId", StringComparison.Ordinal))
            return false;

        var from = Math.Max(0, index - 600);
        return source[from..index].Contains("IntegrationEvent", StringComparison.Ordinal);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = RepositoryRoot();

        foreach (var area in new[] { "src/Services", "src/BuildingBlocks", "src/Gateway" })
        {
            var path = Path.Combine(root, area);
            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (
                    !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                )
                {
                    yield return file;
                }
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
