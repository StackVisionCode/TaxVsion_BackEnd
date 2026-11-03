using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.Profiles;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>Horario semanal: como mucho un tramo por día, ordenado de lunes a domingo. Vacío = sin horario.</summary>
public sealed class WeeklyBusinessHours
{
    public static readonly WeeklyBusinessHours None = new([]);

    public static readonly Error DuplicatedDay = new(
        "Tenant.Profile.BusinessHours",
        "Each day can appear only once in business hours."
    );

    private readonly List<DailyOpeningHours> _days;

    private WeeklyBusinessHours(List<DailyOpeningHours> days) => _days = days;

    public IReadOnlyList<DailyOpeningHours> Days => _days;

    public static Result<WeeklyBusinessHours> Create(IReadOnlyList<BusinessHoursDraft>? drafts)
    {
        if (drafts is null || drafts.Count == 0)
            return Result.Success(None);

        var days = new List<DailyOpeningHours>(drafts.Count);
        foreach (var draft in drafts)
        {
            var day = DailyOpeningHours.Create(draft.Day, draft.Opens, draft.Closes);
            if (day.IsFailure)
                return Result.Failure<WeeklyBusinessHours>(day.Error);
            if (days.Exists(existing => existing.Day == day.Value.Day))
                return Result.Failure<WeeklyBusinessHours>(DuplicatedDay);
            days.Add(day.Value);
        }

        days.Sort((left, right) => left.IsoOrder.CompareTo(right.IsoOrder));
        return Result.Success(new WeeklyBusinessHours(days));
    }

    /// <summary>True si el horario guardado es exactamente este (mismo orden: ambos vienen ordenados).</summary>
    public bool Matches(IReadOnlyList<DailyOpeningHours> current)
    {
        if (current.Count != _days.Count)
            return false;

        for (var i = 0; i < _days.Count; i++)
        {
            if (!_days[i].Matches(current[i]))
                return false;
        }
        return true;
    }
}
