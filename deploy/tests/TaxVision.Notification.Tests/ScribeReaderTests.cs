using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Tests;

/// <summary>Quién lee un correo (Fase 5): su idioma y, si no es la de la oficina, su zona.</summary>
public sealed class ScribeReaderTests
{
    [Theory]
    [InlineData("es", "es")]
    [InlineData("es-MX", "es-MX")]
    [InlineData("Spanish", "es")]
    [InlineData("English", "en")]
    [InlineData("en", "en")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("klingon-x", null)]
    public void The_language_becomes_a_locale_scribe_accepts(string? language, string? expected)
    {
        Assert.Equal(expected, ScribeReader.From(language).Locale);
    }

    [Fact]
    public void Dates_travel_typed_for_scribe()
    {
        Assert.Equal(
            "2026-11-18T14:00:00.0000000Z",
            ScribeDates.Instant(new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc))["$instant"]
        );
        Assert.Equal("2026-11-15", ScribeDates.Date(new DateOnly(2026, 11, 15))["$date"]);
        Assert.Null(ScribeDates.Instant(null));
    }
}
