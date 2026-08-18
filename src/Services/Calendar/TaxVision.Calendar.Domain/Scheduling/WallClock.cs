using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Domain.Scheduling;

/// <summary>
/// El unico convertidor de hora de pared a UTC del servicio.
///
/// <para>
/// Hay dos motores de tiempo a mano —Ical.Net usa la tzdb de NodaTime y <see cref="TimeZoneInfo"/>
/// la del sistema— y en los dos bordes del cambio de hora no coinciden: en la hora ambigua eligen
/// ocurrencias distintas y difieren una hora, sin excepcion y sin log.
/// </para>
///
/// <para>
/// La hora que no existe se rechaza al crear —el usuario eligio una hora que no ocurre y merece
/// saberlo— y corre hacia adelante al expandir, porque una serie no puede romperse un dia al ano. La
/// que ocurre dos veces resuelve a la primera. <c>TimeZoneInfo.ConvertTimeToUtc</c> elige la segunda,
/// asi que no se usa aca.
/// </para>
/// </summary>
public static class WallClock
{
    /// <summary>Convierte una hora de pared a UTC. Rechaza la hora que no existe.</summary>
    public static Result<DateTime> ToUtc(DateOnly date, TimeOnly time, CalendarTimeZone timeZone)
    {
        if (!IanaTimeZone.TryFindTimeZone(timeZone.Id, out var zone))
            return Result.Failure<DateTime>(TimingErrors.InvalidTimeZone);

        var wall = date.ToDateTime(time, DateTimeKind.Unspecified);

        return zone.IsInvalidTime(wall)
            ? Result.Failure<DateTime>(TimingErrors.InvalidLocalTime)
            : Result.Success(Resolve(wall, zone));
    }

    /// <summary>
    /// Igual, pero la ocurrencia que cae en el salto corre hacia adelante en vez de romper la serie.
    /// No hay que buscar el ancho del salto: <c>GetUtcOffset</c> sobre una hora inexistente devuelve
    /// el offset anterior a la transicion, y restarlo ya da la hora corrida.
    /// </summary>
    public static Result<DateTime> ToUtcShiftingOverGaps(DateOnly date, TimeOnly time, CalendarTimeZone timeZone) =>
        IanaTimeZone.TryFindTimeZone(timeZone.Id, out var zone)
            ? Result.Success(Resolve(date.ToDateTime(time, DateTimeKind.Unspecified), zone))
            : Result.Failure<DateTime>(TimingErrors.InvalidTimeZone);

    /// <summary>El offset mas grande es el que estaba vigente la primera vez que el reloj marco esa hora.</summary>
    private static DateTime Resolve(DateTime wall, TimeZoneInfo zone)
    {
        var offset = zone.GetUtcOffset(wall);

        if (zone.IsAmbiguousTime(wall))
        {
            foreach (var candidate in zone.GetAmbiguousTimeOffsets(wall))
            {
                if (candidate > offset)
                    offset = candidate;
            }
        }

        return DateTime.SpecifyKind(wall - offset, DateTimeKind.Utc);
    }

    /// <summary>UTC a hora de pared. No tiene bordes: un instante siempre existe una sola vez.</summary>
    public static Result<DateTime> ToWallClock(DateTime utc, CalendarTimeZone timeZone)
    {
        if (utc.Kind != DateTimeKind.Utc)
            return Result.Failure<DateTime>(TimingErrors.NotUtc);

        return IanaTimeZone.TryFindTimeZone(timeZone.Id, out var zone)
            ? Result.Success(TimeZoneInfo.ConvertTimeFromUtc(utc, zone))
            : Result.Failure<DateTime>(TimingErrors.InvalidTimeZone);
    }
}
