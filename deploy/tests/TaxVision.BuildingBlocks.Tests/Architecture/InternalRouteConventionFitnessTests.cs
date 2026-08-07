using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// GW-02 del plan de remediación — la convención de rutas internas, hecha invariante.
///
/// <para>
/// El guard del Gateway (<c>InternalSurfaceGuardMiddleware</c>, GW-01) protege por comportamiento:
/// devuelve 404 a todo path con segmento <c>internal</c>. Estas dos reglas protegen por estructura,
/// que es lo que sobrevive a que alguien borre el middleware: si ningún controller interno lleva el
/// prefijo del servicio delante, y ninguna ruta del Gateway menciona <c>internal</c>, entonces no
/// existe camino desde internet hasta la superficie M2M — con guard o sin él.
/// </para>
///
/// <para>
/// Se escanean los fuentes en vez de reflejar sobre los assemblies porque el invariante es sobre el
/// repo entero (18 controllers repartidos en 8 servicios) y este proyecto no referencia ninguna Api.
/// </para>
/// </summary>
public sealed class InternalRouteConventionFitnessTests
{
    private static readonly Regex RouteAttribute = new("""\[Route\("(?<template>[^"]*)"\)\]""", RegexOptions.Compiled);

    [Fact]
    public void Todo_Route_con_segmento_internal_empieza_por_internal()
    {
        var violations = new List<string>();

        foreach (var file in SourceFilesUnder("src"))
        {
            foreach (Match match in RouteAttribute.Matches(File.ReadAllText(file)))
            {
                var template = match.Groups["template"].Value;
                var segments = template.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (!segments.Any(s => s.Equals("internal", StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (!segments[0].Equals("internal", StringComparison.OrdinalIgnoreCase))
                    violations.Add($"{Path.GetFileName(file)}: [Route(\"{template}\")]");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Rutas internas con prefijo de servicio delante — el Gateway las alcanza por su catch-all. "
                + "Deben empezar por 'internal/' (GW-02): "
                + string.Join(" · ", violations)
        );
    }

    [Fact]
    public void Ninguna_ruta_del_Gateway_menciona_internal()
    {
        var appsettings = Path.Combine(RepoRoot().FullName, "src", "Gateway", "TaxVision.Gateway", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(appsettings));

        var violations = document
            .RootElement.GetProperty("ReverseProxy")
            .GetProperty("Routes")
            .EnumerateObject()
            .Select(route => (route.Name, Path: route.Value.GetProperty("Match").GetProperty("Path").GetString() ?? ""))
            .Where(r =>
                r.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Any(s => s.Equals("internal", StringComparison.OrdinalIgnoreCase))
            )
            .Select(r => $"{r.Name} → {r.Path}")
            .ToList();

        Assert.True(
            violations.Count == 0,
            "El Gateway declara rutas hacia la superficie interna: " + string.Join(" · ", violations)
        );
    }

    /// <summary>
    /// GW-10 — un <c>MaxRequestBodySize</c> de ruta por encima del límite global de Kestrel es
    /// letra muerta: Kestrel corta antes de que YARP llegue a mirarlo. Este test fija el techo real
    /// (~28,6 MB, el default que el Gateway ya no sobrescribe) para que subir un límite de ruta sin
    /// subir el global falle acá y no en producción con un 413 inexplicable.
    /// </summary>
    [Fact]
    public void Ningun_limite_de_ruta_supera_el_default_de_Kestrel()
    {
        const long kestrelDefaultBytes = 30_000_000;

        var appsettings = Path.Combine(RepoRoot().FullName, "src", "Gateway", "TaxVision.Gateway", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(appsettings));

        var violations = document
            .RootElement.GetProperty("ReverseProxy")
            .GetProperty("Routes")
            .EnumerateObject()
            .Where(route => route.Value.TryGetProperty("MaxRequestBodySize", out _))
            .Select(route => (route.Name, Bytes: route.Value.GetProperty("MaxRequestBodySize").GetInt64()))
            .Where(r => r.Bytes > kestrelDefaultBytes)
            .Select(r => $"{r.Name} = {r.Bytes:N0} B")
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Rutas con MaxRequestBodySize por encima del default de Kestrel ({kestrelDefaultBytes:N0} B) — "
                + "el límite no llega a aplicarse: "
                + string.Join(" · ", violations)
        );
    }

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        return dir
            ?? throw new InvalidOperationException(
                "Could not locate the repo root (TaxVision.slnx) from the test output directory."
            );
    }

    private static IEnumerable<string> SourceFilesUnder(string repoRelativeDir) =>
        Directory
            .EnumerateFiles(Path.Combine(RepoRoot().FullName, repoRelativeDir), "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            );
}
