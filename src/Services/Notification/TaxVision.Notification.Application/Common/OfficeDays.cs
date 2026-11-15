using BuildingBlocks.TimeZones;

namespace TaxVision.Notification.Application.Common;

/// <summary>"Faltan N días" en el calendario de la oficina; una fecha pasada son 0 días.</summary>
public static class OfficeDays
{
    public static int Until(DateTime endUtc, DateTime nowUtc, ResolvedTimeZone zone) =>
        Math.Max(0, CivilDays.Between(nowUtc, endUtc, zone));
}
