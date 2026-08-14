using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>
/// Cuando ocurre un evento. Es el value object donde este servicio se gana o se pierde.
///
/// <para>
/// Un recurrente nunca se guarda en UTC. «Todos los lunes a las 9:00» es hora de pared: guardado como
/// 13:00 UTC porque Nueva York esta en UTC-4 en verano, al entrar el invierno la reunion aparece a
/// las 8:00. Por eso <see cref="StartUtc"/> es null en las series — parece un bug y es la regla.
/// </para>
///
/// <para>
/// All-day es una fecha, no un instante: guardado como medianoche UTC, quien esta en UTC-5 ve el 4 de
/// julio el dia 3. <see cref="EndDate"/> es inclusiva de cara al usuario.
/// </para>
/// </summary>
public sealed record EventTiming
{
    public const int MaxDurationDays = 30;

    public TimingKind Kind { get; }

    /// <summary>Siempre presente: hasta un instante absoluto se muestra en alguna zona.</summary>
    public CalendarTimeZone TimeZone { get; }

    public DateTime? StartUtc { get; }
    public DateTime? EndUtc { get; }

    public DateOnly? StartDate { get; }
    public DateOnly? EndDate { get; }

    public TimeOnly? LocalStartTime { get; }
    public DateOnly? SeriesStartDate { get; }
    public TimeSpan? Duration { get; }

    private EventTiming(
        TimingKind kind,
        CalendarTimeZone timeZone,
        DateTime? startUtc = null,
        DateTime? endUtc = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        TimeOnly? localStartTime = null,
        DateOnly? seriesStartDate = null,
        TimeSpan? duration = null
    )
    {
        Kind = kind;
        TimeZone = timeZone;
        StartUtc = startUtc;
        EndUtc = endUtc;
        StartDate = startDate;
        EndDate = endDate;
        LocalStartTime = localStartTime;
        SeriesStartDate = seriesStartDate;
        Duration = duration;
    }

    public static Result<EventTiming> PointInTimeOf(DateTime startUtc, DateTime endUtc, string? timeZoneId)
    {
        var timeZone = CalendarTimeZone.Create(timeZoneId);
        if (timeZone.IsFailure)
            return Result.Failure<EventTiming>(timeZone.Error);

        // La trampa no es crear la cita: es releerla. EF devuelve datetime2 como Unspecified, asi que
        // sin los convertidores del DbContext una cita valida se rechaza a si misma al volver.
        if (startUtc.Kind != DateTimeKind.Utc || endUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<EventTiming>(TimingErrors.NotUtc);

        if (endUtc <= startUtc)
            return Result.Failure<EventTiming>(TimingErrors.EndBeforeStart);

        if (endUtc - startUtc > TimeSpan.FromDays(MaxDurationDays))
            return Result.Failure<EventTiming>(TimingErrors.DurationTooLong);

        return Result.Success(
            new EventTiming(TimingKind.PointInTime, timeZone.Value, startUtc: startUtc, endUtc: endUtc)
        );
    }

    public static Result<EventTiming> AllDayOf(DateOnly startDate, DateOnly endDate, string? timeZoneId)
    {
        var timeZone = CalendarTimeZone.Create(timeZoneId);
        if (timeZone.IsFailure)
            return Result.Failure<EventTiming>(timeZone.Error);

        // Criterio inclusivo: del 4 al 4 es un dia valido, no un rango invertido.
        if (endDate < startDate)
            return Result.Failure<EventTiming>(TimingErrors.EndBeforeStart);

        if (endDate.DayNumber - startDate.DayNumber >= MaxDurationDays)
            return Result.Failure<EventTiming>(TimingErrors.DurationTooLong);

        return Result.Success(
            new EventTiming(TimingKind.AllDay, timeZone.Value, startDate: startDate, endDate: endDate)
        );
    }

    public static Result<EventTiming> RecurringOf(
        DateOnly seriesStartDate,
        TimeOnly localStartTime,
        TimeSpan duration,
        string? timeZoneId
    )
    {
        var timeZone = CalendarTimeZone.Create(timeZoneId);
        if (timeZone.IsFailure)
            return Result.Failure<EventTiming>(timeZone.Error);

        if (duration <= TimeSpan.Zero)
            return Result.Failure<EventTiming>(TimingErrors.EndBeforeStart);

        if (duration > TimeSpan.FromDays(MaxDurationDays))
            return Result.Failure<EventTiming>(TimingErrors.DurationTooLong);

        return Result.Success(
            new EventTiming(
                TimingKind.Recurring,
                timeZone.Value,
                localStartTime: localStartTime,
                seriesStartDate: seriesStartDate,
                duration: duration
            )
        );
    }

    /// <summary>
    /// Una instancia nueva con los mismos valores: al partir una serie, pasarle a la mitad nueva la
    /// instancia de la vieja dejaria una de las dos sin timing en la base de datos.
    /// </summary>
    internal EventTiming Copy() =>
        new(Kind, TimeZone, StartUtc, EndUtc, StartDate, EndDate, LocalStartTime, SeriesStartDate, Duration);

    /// <summary>Lo mismo que verifica la fitness function sobre datos reales.</summary>
    public bool IsStoredAsWallClock => Kind == TimingKind.Recurring && StartUtc is null && LocalStartTime is not null;
}
