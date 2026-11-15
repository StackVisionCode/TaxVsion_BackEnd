using System.Globalization;
using BuildingBlocks.TimeZones;
using BuildingBlocks.TimeZones.Formatting;

namespace TaxVision.Scribe.Infrastructure.Rendering;

/// <summary>
/// Fecha tipada de una plantilla (P18): se imprime en la zona y el idioma de quien lee. Así ninguna plantilla,
/// sembrada o de una oficina, tiene que saber de zonas, y los nombres de variable no cambian.
/// </summary>
public sealed class ZonedDate
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");

    private readonly DateTime? _instantUtc;
    private readonly DateOnly? _civil;
    private readonly ResolvedTimeZone _zone;
    private readonly string? _language;

    private ZonedDate(DateTime? instantUtc, DateOnly? civil, ResolvedTimeZone zone, string? language)
    {
        _instantUtc = instantUtc;
        _civil = civil;
        _zone = zone;
        _language = language;
    }

    public static ZonedDate Instant(DateTime instantUtc, ResolvedTimeZone zone, string? language) =>
        new(DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc), null, zone, language);

    /// <summary>Fecha civil (un vencimiento): se imprime tal cual, sin pasar por ninguna zona.</summary>
    public static ZonedDate Civil(DateOnly date, string? language) => new(null, date, ResolvedTimeZone.Utc, language);

    private bool IsSpanish => _language is not null && _language.StartsWith("es", StringComparison.OrdinalIgnoreCase);

    /// <summary>Formato P15: "Wed, Nov 18, 2026, 9:00 AM · New York time, GMT-5"; una fecha civil, "Nov 18, 2026".</summary>
    public override string ToString() =>
        _instantUtc is { } instant
            ? ZonedTimeFormatter.DateTime(instant, _zone, _language)
            : ZonedTimeFormatter.CivilDate(_civil!.Value, _language);

    public string DateText() =>
        _instantUtc is { } instant
            ? ZonedTimeFormatter.Date(instant, _zone, _language)
            : ZonedTimeFormatter.CivilDate(_civil!.Value, _language);

    public string TimeText() =>
        _instantUtc is { } instant
            ? TimeZoneInfo
                .ConvertTimeFromUtc(instant, _zone.Zone)
                .ToString(IsSpanish ? "H:mm" : "h:mm tt", IsSpanish ? Spanish : English)
            : string.Empty;

    /// <summary>P14: hasta una hora, en minutos; si falta más, relativo + la fecha con su zona. Vencido o civil: la fecha.</summary>
    public string Deadline(DateTime nowUtc)
    {
        if (_instantUtc is not { } instant || instant <= nowUtc)
            return ToString();

        var left = instant - nowUtc;
        if (left <= TimeSpan.FromHours(1))
            return Relative((int)Math.Ceiling(left.TotalMinutes), "minute", "minuto");

        var relative =
            left < TimeSpan.FromHours(48)
                ? Relative((int)Math.Round(left.TotalHours), "hour", "hora")
                : Relative((int)Math.Round(left.TotalDays), "day", "día");
        return IsSpanish ? $"{relative}, el {this}" : $"{relative}, on {this}";
    }

    private string Relative(int amount, string english, string spanish) =>
        IsSpanish
            ? $"en {amount} {spanish}{(amount == 1 ? "" : "s")}"
            : $"in {amount} {english}{(amount == 1 ? "" : "s")}";
}
