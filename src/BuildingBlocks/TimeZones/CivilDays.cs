namespace BuildingBlocks.TimeZones;

/// <summary>Días de calendario entre dos instantes vistos en una zona, no bloques de 24 h.</summary>
public static class CivilDays
{
    public static int Between(DateTime fromUtc, DateTime toUtc, ResolvedTimeZone zone) =>
        CivilDate.Of(toUtc, zone).DayNumber - CivilDate.Of(fromUtc, zone).DayNumber;
}
