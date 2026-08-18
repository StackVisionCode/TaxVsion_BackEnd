using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>
/// Zona horaria IANA de un evento. Envuelve <see cref="IanaTimeZone"/> —que ya resuelve el mapeo
/// IANA↔Windows— para que el dominio no repita esa validación en cada llamador.
/// </summary>
public sealed record CalendarTimeZone
{
    public string Id { get; }

    private CalendarTimeZone(string id) => Id = id;

    public static Result<CalendarTimeZone> Create(string? timeZoneId)
    {
        var trimmed = timeZoneId?.Trim() ?? string.Empty;

        if (!IsRegionForm(trimmed) || !IanaTimeZone.TryNormalize(trimmed, out var normalized))
            return Result.Failure<CalendarTimeZone>(TimingErrors.InvalidTimeZone);

        return Result.Success(new CalendarTimeZone(normalized));
    }

    /// <summary>
    /// Exige la forma canonica <c>Area/Location</c> y rechaza las abreviaturas sueltas.
    ///
    /// <para>
    /// No es purismo: medido, <c>EST</c> resuelve a «SA Pacific Standard Time» —Bogota, UTC-5
    /// <b>sin horario de verano</b>—, asi que quien escribe <c>EST</c> pensando en Nueva York recibe
    /// una zona que nunca cambia de hora y sus citas salen corridas medio ano. <c>MST</c> cae en
    /// Arizona y <c>HST</c> en Hawai por el mismo camino. Las formas con region
    /// (<c>America/New_York</c>, y tambien los alias viejos como <c>US/Eastern</c>) llevan las reglas
    /// de DST correctas.
    /// </para>
    /// </summary>
    private static bool IsRegionForm(string timeZoneId) => timeZoneId.Contains('/');

    public override string ToString() => Id;
}
