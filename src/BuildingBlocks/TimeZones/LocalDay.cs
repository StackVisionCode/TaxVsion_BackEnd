namespace BuildingBlocks.TimeZones;

/// <summary>Límites de un día civil en una zona, como instantes UTC. Respeta días de 23 y 25 horas.</summary>
public static class LocalDay
{
    /// <summary>Medianoche local; si no existe por el horario de verano, el primer instante del día.</summary>
    public static DateTime StartUtc(DateOnly date, ResolvedTimeZone zone) =>
        WallTime.ToUtc(date, TimeOnly.MinValue, zone, NonexistentTimePolicy.ShiftForward).Value;

    /// <summary>Último segundo del día local (23:59:59), la convención de los vencimientos de "fin del día".</summary>
    public static DateTime EndUtc(DateOnly date, ResolvedTimeZone zone) => StartUtc(date.AddDays(1), zone).AddSeconds(-1);
}
