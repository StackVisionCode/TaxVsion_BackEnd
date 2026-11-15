using BuildingBlocks.TimeZones;

namespace TaxVision.Calendar.Domain.Scheduling;

/// <summary>
/// P23 (D-TZ02-6): "reservable hasta N días" termina a la medianoche local que cierra el día N, contando desde el día
/// local de hoy. Una zona desconocida cuenta días UTC.
/// </summary>
public static class BookingHorizon
{
    public static DateTime EndUtc(DateTime nowUtc, int maxDaysAhead, string? timeZoneId)
    {
        var zone = ResolvedTimeZone.Resolve(timeZoneId);
        return LocalDay.StartUtc(CivilDate.Of(nowUtc, zone).AddDays(maxDaysAhead + 1), zone);
    }
}
