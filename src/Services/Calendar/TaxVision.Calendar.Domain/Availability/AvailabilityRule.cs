using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Domain.Availability;

/// <summary>
/// La franja en que una persona acepta citas: «lunes a viernes, 9:00 a 17:00».
///
/// <para>
/// Es hora de pared con su zona, por la misma razon que una serie: si se guardara en UTC, el horario
/// de atencion se correria una hora al cambiar el horario y la oficina abriria a las 8.
/// </para>
/// </summary>
public sealed class AvailabilityRule : AggregateRoot
{
    public Guid UserId { get; private set; }

    public CalendarTimeZone TimeZone { get; private set; } = default!;

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    /// <summary>Mascara de dias; se guarda como entero para poder filtrar en SQL.</summary>
    public DaysOfWeekMask Days { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private AvailabilityRule() { }

    public static Result<AvailabilityRule> Create(
        Guid tenantId,
        Guid userId,
        DaysOfWeekMask days,
        TimeOnly startTime,
        TimeOnly endTime,
        string? timeZoneId,
        DateTime nowUtc
    )
    {
        if (userId == Guid.Empty)
            return Result.Failure<AvailabilityRule>(AvailabilityErrors.UserRequired);

        if (days == DaysOfWeekMask.None)
            return Result.Failure<AvailabilityRule>(AvailabilityErrors.NoDays);

        if (endTime <= startTime)
            return Result.Failure<AvailabilityRule>(AvailabilityErrors.EndBeforeStart);

        var timeZone = CalendarTimeZone.Create(timeZoneId);
        if (timeZone.IsFailure)
            return Result.Failure<AvailabilityRule>(timeZone.Error);

        var rule = new AvailabilityRule
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TimeZone = timeZone.Value,
            StartTime = startTime,
            EndTime = endTime,
            Days = days,
            IsActive = true,
            CreatedAtUtc = nowUtc,
        };
        rule.SetTenant(tenantId);

        return Result.Success(rule);
    }

    public bool AppliesTo(DayOfWeek day) => Days.Includes(day);

    public void Deactivate() => IsActive = false;
}
