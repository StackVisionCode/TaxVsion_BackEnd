using System.Globalization;

namespace BuildingBlocks.TimeZones.Formatting;

/// <summary>
/// Formato único de fechas para correos, PDF y textos (P15): "Wed, Nov 18, 2026, 9:00 AM · New York time, GMT-5".
/// La zona se rotula con la ciudad y el GMT del instante, sin abreviaturas (EST/CST no son fiables ni únicas).
/// La cultura es explícita: no depende de la del proceso.
/// </summary>
public static class ZonedTimeFormatter
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");
    private static readonly HashSet<string> UtcIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Etc/UTC", "UTC", "Etc/GMT", "GMT", "Etc/Universal", "Universal", "Etc/Zulu", "Zulu",
    };

    public static string DateTime(System.DateTime instantUtc, ResolvedTimeZone zone, string? language = null)
    {
        var local = Local(instantUtc, zone);
        var text = IsSpanish(language)
            ? local.ToString("d 'de' MMMM 'de' yyyy, H:mm", Spanish)
            : local.ToString("ddd, MMM d, yyyy, h:mm tt", English);
        return $"{text} · {ZoneLabel(zone, instantUtc, language)}";
    }

    /// <summary>Fecha local de un instante, sin hora: "Nov 18, 2026".</summary>
    public static string Date(System.DateTime instantUtc, ResolvedTimeZone zone, string? language = null) =>
        CivilDate(TimeZones.CivilDate.Of(instantUtc, zone), language);

    /// <summary>Fecha civil tal cual, sin conversión de zona.</summary>
    public static string CivilDate(DateOnly date, string? language = null) =>
        IsSpanish(language) ? date.ToString("d 'de' MMMM 'de' yyyy", Spanish) : date.ToString("MMM d, yyyy", English);

    /// <summary>"New York time, GMT-5" / "hora de New York, GMT-5"; "UTC" para una oficina en UTC.</summary>
    public static string ZoneLabel(ResolvedTimeZone zone, System.DateTime instantUtc, string? language = null)
    {
        if (UtcIds.Contains(zone.Id) || zone.Zone == TimeZoneInfo.Utc)
            return "UTC";
        var city = zone.Id[(zone.Id.LastIndexOf('/') + 1)..].Replace('_', ' ');
        var gmt = Gmt(zone.Zone.GetUtcOffset(TimeZones.CivilDate.AsUtc(instantUtc)));
        return IsSpanish(language) ? $"hora de {city}, {gmt}" : $"{city} time, {gmt}";
    }

    private static System.DateTime Local(System.DateTime instantUtc, ResolvedTimeZone zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(TimeZones.CivilDate.AsUtc(instantUtc), zone.Zone);

    private static bool IsSpanish(string? language) =>
        language is not null && language.StartsWith("es", StringComparison.OrdinalIgnoreCase);

    /// <summary>"GMT", "GMT-4", "GMT+5:30": el mismo formato que <c>Intl</c> (shortOffset).</summary>
    private static string Gmt(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero)
            return "GMT";
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return abs.Minutes == 0
            ? $"GMT{sign}{abs.Hours}"
            : $"GMT{sign}{abs.Hours}:{abs.Minutes.ToString("00", CultureInfo.InvariantCulture)}";
    }
}
