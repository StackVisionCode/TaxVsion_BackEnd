using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// Guardrails de tiempo (P22): un servicio en modo invariante no usa zonas ni culturas, y cada servicio .NET registra
/// el chequeo de zonas al arrancar para que el log muestre si el contenedor resuelve las zonas IANA.
/// </summary>
public sealed class TimeZoneInfraFitnessTests
{
    // CultureInfo.InvariantCulture es lo correcto en modo invariante; cualquier otra cultura o zona no.
    private static readonly Regex UsesZonesOrCultures = new(
        @"\bTimeZoneInfo\b|\bCultureInfo\b(?!\.InvariantCulture\b)|\bBuildingBlocks\.TimeZones\b|\bBuildingBlocks\.TenantTimeZones\b|\bResolvedTimeZone\b"
    );

    [Fact]
    public void A_service_in_invariant_mode_does_not_use_time_zones_or_cultures()
    {
        var offenders = new List<string>();
        foreach (
            var csproj in Directory.EnumerateFiles(
                Path.Combine(RepoRoot(), "src", "Services"),
                "*.csproj",
                SearchOption.AllDirectories
            )
        )
        {
            if (
                !File.ReadAllText(csproj)
                    .Contains("<InvariantGlobalization>true</InvariantGlobalization>", StringComparison.Ordinal)
            )
                continue;
            var dir = Path.GetDirectoryName(csproj)!;
            offenders.AddRange(
                Directory
                    .EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    .Where(f => UsesZonesOrCultures.IsMatch(File.ReadAllText(f)))
                    .Select(f => Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/'))
            );
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_service_that_adds_building_blocks_also_adds_the_time_zone_self_check()
    {
        var missing = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "src", "Services"), "Program.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains(".AddBuildingBlocks()", StringComparison.Ordinal)
                    && !text.Contains(".AddTimeZoneSelfCheck()", StringComparison.Ordinal);
            })
            .Select(f => Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Empty(missing);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró TaxVision.slnx.");
    }
}
