using System.Text.Json;
using Xunit;

namespace TaxVision.Gateway.Tests.LoadShedding;

/// <summary>
/// Fitness function de GW-14: añadir una ruta al Gateway sin decidir su criticidad rompe el build.
/// Sin esto la clasificación se degrada sola — la ruta nueva cae en el <c>DefaultCriticality</c> y
/// nadie se entera de que jamás se tomó la decisión.
/// </summary>
public sealed class LoadSheddingCriticalityCoverageTests
{
    private static JsonElement Settings()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "Gateway", "TaxVision.Gateway", "appsettings.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Primer segmento del <c>Match.Path</c>, que es la clave que usa el clasificador.</summary>
    private static IEnumerable<string> RouteSegments(JsonElement settings) =>
        settings
            .GetProperty("ReverseProxy")
            .GetProperty("Routes")
            .EnumerateObject()
            .Select(route => route.Value.GetProperty("Match").GetProperty("Path").GetString() ?? string.Empty)
            .Select(path => path.TrimStart('/').Split('/')[0].ToLowerInvariant())
            .Where(segment => segment.Length > 0)
            .Distinct();

    [Fact]
    public void TodaRutaDelGatewayTieneCriticidadDeclarada()
    {
        var settings = Settings();
        var declared = settings
            .GetProperty("LoadShedding")
            .GetProperty("Criticality")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = RouteSegments(settings).Where(segment => !declared.Contains(segment)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"Rutas del Gateway sin criticidad en LoadShedding:Criticality: {string.Join(", ", missing)}. "
                + "Clasificarlas como Critical (login/alta/cobro), Standard o Background (analitica, "
                + "auditoria, correo saliente) antes de exponerlas."
        );
    }

    [Fact]
    public void NoSobranEntradasDeCriticidadSinRuta()
    {
        // Una entrada huerfana es criticidad que no aplica a nada: la ruta se renombro o se borro y
        // la clasificacion quedo mintiendo.
        var settings = Settings();
        var segments = RouteSegments(settings).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orphans = settings
            .GetProperty("LoadShedding")
            .GetProperty("Criticality")
            .EnumerateObject()
            .Select(p => p.Name)
            .Where(name => !segments.Contains(name))
            .ToArray();

        Assert.True(orphans.Length == 0, $"Criticidad declarada para rutas inexistentes: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void LasCriticidadesDeclaradasSonValoresValidos()
    {
        var invalid = Settings()
            .GetProperty("LoadShedding")
            .GetProperty("Criticality")
            .EnumerateObject()
            .Where(p => !Enum.TryParse<Gateway.LoadShedding.RequestCriticality>(p.Value.GetString(), out _))
            .Select(p => $"{p.Name}={p.Value.GetString()}")
            .ToArray();

        Assert.True(invalid.Length == 0, $"Criticidades no reconocidas: {string.Join(", ", invalid)}");
    }
}
