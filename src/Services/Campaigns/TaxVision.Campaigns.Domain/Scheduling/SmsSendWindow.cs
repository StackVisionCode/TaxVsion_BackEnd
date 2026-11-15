using BuildingBlocks.TimeZones;

namespace TaxVision.Campaigns.Domain.Scheduling;

/// <summary>
/// P12: ventana permitida para los SMS de campañas en la zona del destinatario (o de la oficina). Fuera de ella, el
/// envío espera a la próxima apertura: más tarde el mismo día o al día siguiente.
/// </summary>
public static class SmsSendWindow
{
    /// <summary>Null si <paramref name="atUtc"/> cae dentro de la ventana; si no, el instante de la próxima apertura.</summary>
    public static DateTime? NextOpening(DateTime atUtc, ResolvedTimeZone zone, TimeOnly opens, TimeOnly closes)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc), zone.Zone);
        var time = TimeOnly.FromDateTime(local);
        if (time >= opens && time < closes)
            return null;

        var day = DateOnly.FromDateTime(local);
        var openingDay = time < opens ? day : day.AddDays(1);
        return WallTime.ToUtc(openingDay, opens, zone, NonexistentTimePolicy.ShiftForward).Value;
    }
}
