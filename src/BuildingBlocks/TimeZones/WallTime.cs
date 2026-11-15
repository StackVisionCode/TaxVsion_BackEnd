using BuildingBlocks.Results;

namespace BuildingBlocks.TimeZones;

/// <summary>
/// Hora de pared → instante UTC con la política escrita de Calendar (C-T9): la hora inexistente se rechaza o
/// corre hacia adelante según <see cref="NonexistentTimePolicy"/>; la ambigua toma la primera ocurrencia.
/// </summary>
public static class WallTime
{
    public static Result<DateTime> ToUtc(DateOnly date, TimeOnly time, ResolvedTimeZone zone, NonexistentTimePolicy policy)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var tz = zone.Zone;

        if (tz.IsInvalidTime(local))
        {
            if (policy == NonexistentTimePolicy.Reject)
                return Result.Failure<DateTime>(TimeZoneErrors.NonexistentLocalTime);

            // Con el offset vigente antes del salto se cae del otro lado, corrido el largo del salto.
            var offsetBefore = tz.GetUtcOffset(local.AddHours(-6));
            return Result.Success(Utc(local - offsetBefore));
        }

        if (tz.IsAmbiguousTime(local))
        {
            // La primera ocurrencia es la del offset mayor (la del horario que termina).
            var first = tz.GetAmbiguousTimeOffsets(local).Max();
            return Result.Success(Utc(local - first));
        }

        return Result.Success(Utc(local - tz.GetUtcOffset(local)));
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
