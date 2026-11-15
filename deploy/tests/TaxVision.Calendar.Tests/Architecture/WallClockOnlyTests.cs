extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using Xunit;

namespace TaxVision.Calendar.Tests.Architecture;

/// <summary>
/// P23: <c>TimeZoneInfo.ConvertTimeToUtc</c> elige la segunda ocurrencia de una hora ambigua y difiere de
/// <c>WallClock</c> una hora. Toda hora de pared de Calendar pasa por <c>WallClock</c>.
/// </summary>
public sealed class WallClockOnlyTests
{
    private const string CalendarSources = "src/Services/Calendar";

    [Fact]
    public void Only_the_wall_clock_converts_wall_time_to_utc()
    {
        var offenders = Directory
            .GetFiles(
                Path.Combine(JobPublishingRules.RepositoryRoot(), CalendarSources),
                "*.cs",
                SearchOption.AllDirectories
            )
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.EndsWith("WallClock.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("ConvertTimeToUtc(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }
}
