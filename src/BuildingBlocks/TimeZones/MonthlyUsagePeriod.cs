namespace BuildingBlocks.TimeZones;

/// <summary>
/// Período mensual de un cupo (P11): empieza cada mes el día del ancla (el alta de la suscripción), a medianoche de la
/// oficina. Si el mes es más corto, el último día. Sin ancla, el mes UTC.
/// </summary>
public static class MonthlyUsagePeriod
{
    /// <summary>Inicio del período vigente en <paramref name="nowUtc"/>, como instante UTC. Sirve de clave del período.</summary>
    public static DateTime StartUtc(DateTime nowUtc, DateTime? anchorUtc, ResolvedTimeZone zone)
    {
        if (anchorUtc is not { } anchor)
            return new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var anchorDay = CivilDate.Of(anchor, zone).Day;
        var today = CivilDate.Of(nowUtc, zone);
        var start = DayIn(today.Year, today.Month, anchorDay);
        if (today < start)
        {
            var previous = today.AddMonths(-1);
            start = DayIn(previous.Year, previous.Month, anchorDay);
        }
        return LocalDay.StartUtc(start, zone);
    }

    private static DateOnly DayIn(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}
