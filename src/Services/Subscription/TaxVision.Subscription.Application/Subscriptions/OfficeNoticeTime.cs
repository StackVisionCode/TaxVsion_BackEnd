using BuildingBlocks.TimeZones;

namespace TaxVision.Subscription.Application.Subscriptions;

/// <summary>
/// P28: los avisos programados cuentan días en el calendario de la oficina y salen en la franja de las 9:00 locales.
/// El job corre cada hora, así que cada oficina cae una sola vez por día en esa franja.
/// </summary>
public static class OfficeNoticeTime
{
    public const int SendHour = 9;

    public static int DaysUntil(DateTime dueAtUtc, DateTime nowUtc, ResolvedTimeZone officeZone) =>
        CivilDays.Between(nowUtc, dueAtUtc, officeZone);

    public static bool IsSendHour(DateTime nowUtc, ResolvedTimeZone officeZone) =>
        LocalHour(nowUtc, officeZone) == SendHour;

    // Para los avisos con marca en la base: si se saltó la franja, salen más tarde ese día.
    public static bool IsAtOrAfterSendHour(DateTime nowUtc, ResolvedTimeZone officeZone) =>
        LocalHour(nowUtc, officeZone) >= SendHour;

    private static int LocalHour(DateTime nowUtc, ResolvedTimeZone officeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), officeZone.Zone).Hour;
}
