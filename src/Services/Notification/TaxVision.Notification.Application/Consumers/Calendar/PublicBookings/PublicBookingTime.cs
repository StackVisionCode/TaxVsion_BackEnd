using System.Globalization;
using BuildingBlocks.TimeZones;

namespace TaxVision.Notification.Application.Consumers.Calendar.PublicBookings;

/// <summary>Fecha, hora y zona de la demo como las lee cada persona: en su zona y en su idioma (DEMO-10).</summary>
internal static class PublicBookingTime
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");

    public sealed record Local(string Date, string Time, string ZoneLabel);

    public static Local Describe(DateTime utc, string? timeZoneId, string language)
    {
        var spanish = language == PublicBookingEmails.Spanish;
        var hasZone = IanaTimeZone.TryFindTimeZone(timeZoneId ?? string.Empty, out var zone);
        var instant = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = hasZone ? TimeZoneInfo.ConvertTimeFromUtc(instant, zone!) : utc;
        var label = hasZone ? ZoneLabel(timeZoneId!, zone!.GetUtcOffset(instant), spanish) : "UTC";
        return spanish
            ? new Local(local.ToString("dddd d 'de' MMMM 'de' yyyy", Spanish), local.ToString("H:mm", Spanish), label)
            : new Local(local.ToString("dddd, MMMM d, yyyy", English), local.ToString("h:mm tt", English), label);
    }

    /// <summary>
    /// "New York time, GMT-5" / "hora de New York, GMT-5": la ciudad de la zona IANA y el desfase del día de la
    /// demo. La ciudad sola confunde a quien vive en otro país con la misma hora (Windows da La Paz para todo
    /// UTC-4). Sin abreviaturas (EST, CET): .NET no las trae de forma fiable en Linux.
    /// </summary>
    private static string ZoneLabel(string timeZoneId, TimeSpan offset, bool spanish)
    {
        var city = timeZoneId[(timeZoneId.LastIndexOf('/') + 1)..].Replace('_', ' ');
        var gmt = Gmt(offset);
        return spanish ? $"hora de {city}, {gmt}" : $"{city} time, {gmt}";
    }

    /// <summary>"GMT", "GMT-4", "GMT+5:30": mismo formato que <c>Intl</c> (shortOffset) en el landing.</summary>
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
