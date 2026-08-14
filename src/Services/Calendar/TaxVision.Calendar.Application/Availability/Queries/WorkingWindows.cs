using BuildingBlocks.TimeZones;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Availability.Queries;

/// <summary>
/// Convierte las reglas de horario en intervalos UTC, día por día.
///
/// <para>
/// Recorre en la zona de la regla y no en UTC, por lo mismo que una serie: si el rango cruza el cambio
/// de horario, el 9-17 de la oficina se correría una hora a mitad de semana.
/// </para>
/// </summary>
internal static class WorkingWindows
{
    public static IReadOnlyList<TimeSlot> Build(IReadOnlyList<AvailabilityRule> rules, DateTime fromUtc, DateTime toUtc)
    {
        var windows = new List<TimeSlot>();

        foreach (var rule in rules)
        {
            if (!rule.IsActive || !IanaTimeZone.TryFindTimeZone(rule.TimeZone.Id, out var zone))
                continue;

            var localFrom = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(fromUtc, zone)).AddDays(-1);
            var localTo = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(toUtc, zone)).AddDays(1);

            for (var day = localFrom; day <= localTo; day = day.AddDays(1))
            {
                if (!rule.AppliesTo(day.DayOfWeek))
                    continue;

                var start = WallClock.ToUtcShiftingOverGaps(day, rule.StartTime, rule.TimeZone);
                var end = WallClock.ToUtcShiftingOverGaps(day, rule.EndTime, rule.TimeZone);

                if (start.IsSuccess && end.IsSuccess && end.Value > start.Value)
                    windows.Add(new TimeSlot(start.Value, end.Value));
            }
        }

        windows.Sort((left, right) => left.StartUtc.CompareTo(right.StartUtc));
        return windows;
    }
}
