using BuildingBlocks.Results;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Domain.Scheduling;

/// <summary>Una ocurrencia calculada. No es una fila: no existe tabla de ocurrencias.</summary>
public sealed record Occurrence(
    Guid AppointmentId,
    DateTime OriginalStartUtc,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsException,
    string Title,
    string? Location
);

/// <summary>
/// Convierte una serie en las ocurrencias de un rango, al vuelo: una serie de tres años son 156
/// ocurrencias y una sola fila.
///
/// <para>El orden de estas seis operaciones es la correccion del servicio, no una preferencia:</para>
///
/// <list type="number">
/// <item>Expandir el RRULE <b>EN LA ZONA</b> de la serie — el DST se aplica fecha por fecha.</item>
/// <item>Convertir cada ocurrencia a UTC, con el motor unico.</item>
/// <item>Quitar las que tienen excepción <c>Cancelled</c>.</item>
/// <item>Reemplazar los campos de las que tienen excepción <c>Overridden</c>.</item>
/// <item>Recortar al rango pedido.</item>
/// <item>Cap de <see cref="MaxOccurrencesPerQuery"/>: falla explícito, nunca cuelgue.</item>
/// </list>
///
/// <para>
/// Invertir 1 y 2 es el bug de DST: expandir en UTC congela el offset del primer dia y corre la serie
/// entera una hora al cambiar el horario, dos veces al año y en silencio.
/// </para>
/// </summary>
public static class OccurrenceExpander
{
    public const int MaxOccurrencesPerQuery = 1000;

    public static Result<IReadOnlyList<Occurrence>> Expand(
        Appointment appointment,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc
    )
    {
        if (rangeStartUtc.Kind != DateTimeKind.Utc || rangeEndUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<IReadOnlyList<Occurrence>>(TimingErrors.NotUtc);

        if (rangeEndUtc <= rangeStartUtc)
            return Result.Failure<IReadOnlyList<Occurrence>>(RecurrenceErrors.RangeInverted);

        return appointment.Recurrence is null
            ? ExpandSingle(appointment, rangeStartUtc, rangeEndUtc)
            : ExpandSeries(appointment, rangeStartUtc, rangeEndUtc);
    }

    /// <summary>Una cita puntual es su propia ocurrencia, o ninguna si cae fuera del rango.</summary>
    private static Result<IReadOnlyList<Occurrence>> ExpandSingle(
        Appointment appointment,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc
    )
    {
        var timing = appointment.Timing;
        if (timing.StartUtc is not { } startUtc || timing.EndUtc is not { } endUtc)
            return Result.Success<IReadOnlyList<Occurrence>>([]);

        if (endUtc <= rangeStartUtc || startUtc >= rangeEndUtc)
            return Result.Success<IReadOnlyList<Occurrence>>([]);

        return Result.Success<IReadOnlyList<Occurrence>>([
            new Occurrence(
                appointment.Id,
                startUtc,
                startUtc,
                endUtc,
                IsException: false,
                appointment.Title.Value,
                appointment.Location?.Value
            ),
        ]);
    }

    private static Result<IReadOnlyList<Occurrence>> ExpandSeries(
        Appointment appointment,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc
    )
    {
        var timing = appointment.Timing;

        if (timing.SeriesStartDate is null || timing.LocalStartTime is null || timing.Duration is not { } duration)
            return Result.Failure<IReadOnlyList<Occurrence>>(TimingErrors.RecurringMustBeLocal);

        if (!appointment.Recurrence!.TryBuildPattern(out _))
            return Result.Failure<IReadOnlyList<Occurrence>>(RecurrenceErrors.RuleInvalid);

        var exceptions = IndexExceptions(appointment);
        var results = new List<Occurrence>();

        // (1) y (2) viven en EnumerateRuleStarts. El corte en rangeEndUtc es lo que hace terminar a
        // una serie sin UNTIL.
        foreach (var originalStartUtc in EnumerateRuleStarts(appointment, rangeEndUtc))
        {
            var occurrence = Materialize(appointment, originalStartUtc, duration, exceptions);

            // (3) Cancelled: la ocurrencia desaparece.
            if (occurrence is null)
                continue;

            // (5) El recorte va sobre el instante EFECTIVO: una ocurrencia movida hacia adentro del
            // rango tiene que aparecer, y una movida hacia afuera tiene que irse.
            if (occurrence.EndUtc <= rangeStartUtc || occurrence.StartUtc >= rangeEndUtc)
                continue;

            results.Add(occurrence);

            // (6) Falla explícito. Colgarse expandiendo una serie diaria de veinte años es peor.
            if (results.Count > MaxOccurrencesPerQuery)
                return Result.Failure<IReadOnlyList<Occurrence>>(RecurrenceErrors.RangeTooLarge);
        }

        AddOccurrencesMovedBackIntoRange(appointment, rangeStartUtc, rangeEndUtc, duration, results);

        return Result.Success<IReadOnlyList<Occurrence>>(results);
    }

    /// <summary>
    /// El recorrido de la regla se detiene en el fin del rango, así que una ocurrencia <b>movida hacia
    /// atrás</b> desde más allá se perdería: el preparador adelanta la cita del 20 de marzo al 25 de
    /// febrero y desaparece del calendario de febrero. Son pocas y ya están cargadas, así que se
    /// repasan aparte en vez de ensanchar el recorrido entero.
    /// </summary>
    private static void AddOccurrencesMovedBackIntoRange(
        Appointment appointment,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        TimeSpan duration,
        List<Occurrence> results
    )
    {
        foreach (var exception in appointment.Exceptions)
        {
            // Las anteriores al fin del rango ya pasaron por el recorrido normal.
            if (exception.Kind != ExceptionKind.Overridden || exception.OriginalStartUtc < rangeEndUtc)
                continue;

            var startUtc = exception.NewStartUtc ?? exception.OriginalStartUtc;
            var endUtc = exception.NewEndUtc ?? startUtc + duration;

            if (endUtc <= rangeStartUtc || startUtc >= rangeEndUtc)
                continue;

            results.Add(
                new Occurrence(
                    appointment.Id,
                    exception.OriginalStartUtc,
                    startUtc,
                    endUtc,
                    IsException: true,
                    exception.NewTitle ?? appointment.Title.Value,
                    exception.NewLocation ?? appointment.Location?.Value
                )
            );
        }
    }

    /// <summary>(4) Aplica la excepción si la hay. Devuelve <c>null</c> cuando la ocurrencia se canceló.</summary>
    private static Occurrence? Materialize(
        Appointment appointment,
        DateTime originalStartUtc,
        TimeSpan duration,
        Dictionary<DateTime, AppointmentException> exceptions
    )
    {
        var title = appointment.Title.Value;
        var location = appointment.Location?.Value;

        if (!exceptions.TryGetValue(originalStartUtc, out var exception))
        {
            return new Occurrence(
                appointment.Id,
                originalStartUtc,
                originalStartUtc,
                originalStartUtc + duration,
                IsException: false,
                title,
                location
            );
        }

        if (exception.Kind == ExceptionKind.Cancelled)
            return null;

        var startUtc = exception.NewStartUtc ?? originalStartUtc;
        var endUtc = exception.NewEndUtc ?? startUtc + duration;

        return new Occurrence(
            appointment.Id,
            originalStartUtc,
            startUtc,
            endUtc,
            IsException: true,
            exception.NewTitle ?? title,
            exception.NewLocation ?? location
        );
    }

    private static Dictionary<DateTime, AppointmentException> IndexExceptions(Appointment appointment)
    {
        var index = new Dictionary<DateTime, AppointmentException>();

        foreach (var exception in appointment.Exceptions)
            index[exception.OriginalStartUtc] = exception;

        return index;
    }

    /// <summary>
    /// ¿Este instante es una ocurrencia que la serie produce de verdad? Sin comprobarlo, un cliente con
    /// un bug llena la tabla de excepciones para fechas que nunca existieron.
    ///
    /// <para>
    /// Pregunta por lo que produce la regla, no por el resultado de <see cref="Expand"/>: una
    /// ocurrencia ya cancelada, o movida fuera del rango, sigue siendo una ocurrencia real.
    /// </para>
    /// </summary>
    public static bool IsOccurrence(Appointment appointment, DateTime candidateUtc)
    {
        if (candidateUtc.Kind != DateTimeKind.Utc || appointment.Recurrence is null)
            return false;

        foreach (var startUtc in EnumerateRuleStarts(appointment, candidateUtc.AddSeconds(1)))
        {
            if (startUtc == candidateUtc)
                return true;
        }

        return false;
    }

    /// <summary>
    /// La primera ocurrencia que produce la regla, o null si no produce ninguna. Partir sobre ella
    /// dejaria la serie vieja vacia.
    /// </summary>
    public static DateTime? FirstStart(Appointment appointment)
    {
        foreach (var startUtc in EnumerateRuleStarts(appointment, DateTime.MaxValue))
            return startUtc;

        return null;
    }

    /// <summary>
    /// Los inicios que produce la regla, en UTC y en orden. Es la unica parte que ve Ical.Net y la
    /// unica que convierte.
    /// </summary>
    private static IEnumerable<DateTime> EnumerateRuleStarts(Appointment appointment, DateTime stopAtUtc)
    {
        var timing = appointment.Timing;

        if (
            timing.SeriesStartDate is not { } seriesStart
            || timing.LocalStartTime is not { } localStart
            || !appointment.Recurrence!.TryBuildPattern(out var pattern)
        )
        {
            yield break;
        }

        var zone = timing.TimeZone;
        var seed = new CalDateTime(seriesStart.ToDateTime(localStart, DateTimeKind.Unspecified), zone.Id);
        var series = new CalendarEvent { Start = seed, RecurrenceRules = [pattern] };

        foreach (var raw in series.GetOccurrences(seed))
        {
            var local = raw.Period.StartTime.Value;
            var converted = WallClock.ToUtcShiftingOverGaps(
                DateOnly.FromDateTime(local),
                TimeOnly.FromDateTime(local),
                zone
            );

            if (converted.IsFailure)
                yield break;

            if (converted.Value >= stopAtUtc)
                yield break;

            yield return converted.Value;
        }
    }
}
