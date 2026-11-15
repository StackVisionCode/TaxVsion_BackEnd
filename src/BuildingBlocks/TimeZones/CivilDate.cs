namespace BuildingBlocks.TimeZones;

/// <summary>Fecha civil de un instante en una zona: "qué día es" allí.</summary>
public static class CivilDate
{
    /// <summary>Un instante con <c>Kind</c> sin especificar se lee como UTC (convención de los campos <c>...Utc</c>).</summary>
    public static DateOnly Of(DateTime instantUtc, ResolvedTimeZone zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(instantUtc), zone.Zone));

    internal static DateTime AsUtc(DateTime instant) =>
        instant.Kind switch
        {
            DateTimeKind.Utc => instant,
            DateTimeKind.Local => instant.ToUniversalTime(),
            _ => DateTime.SpecifyKind(instant, DateTimeKind.Utc),
        };
}
