using BuildingBlocks.TimeZones;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// Pinta un instante UTC en la zona de la cita.
///
/// <para>
/// No en la del destinatario: un asistente externo no tiene perfil donde mirarla, y Notification no
/// tiene directorio de usuarios internos. Poner la zona al lado de la hora es lo unico que evita el
/// correo ambiguo — «10:00» sin mas es exactamente lo que hace que alguien llegue con una hora de
/// diferencia.
/// </para>
/// </summary>
internal static class CalendarTimeFormatting
{
    public static string InZone(DateTime utc, string timeZoneId)
    {
        if (!IanaTimeZone.TryFindTimeZone(timeZoneId, out var zone))
            return utc.ToString("yyyy-MM-dd HH:mm 'UTC'");

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        return local.ToString("yyyy-MM-dd HH:mm");
    }
}
