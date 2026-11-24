using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Series;

/// <summary>Las fechas que produciría una serie, en días de la oficina, con lo que conviene avisar.</summary>
public sealed record RecurrencePreviewResult(IReadOnlyList<DateOnly> Dates, IReadOnlyList<string> Warnings);

/// <summary>
/// Vista previa con el mismo motor que crea las tareas. La primera fecha se busca sin INTERVAL (si no, una regla "cada
/// 2 semanas" sembrada en la víspera saltaría la primera); las siguientes se encadenan desde la anterior, como la serie
/// desde <see cref="TaskSeries.LastPlannedDueUtc"/>. En "después de completar" se supone cada una completada a tiempo.
/// </summary>
public static class RecurrencePreview
{
    public const int MaxCount = 12;

    public static RecurrencePreviewResult Compute(
        RecurrenceRule rule,
        RecurrenceMode mode,
        DateOnly startsOn,
        DateOnly? endsOn,
        int? maxOccurrences,
        int count
    )
    {
        var zone = ResolvedTimeZone.Resolve(rule.TimeZoneId);
        var limit = Math.Clamp(Math.Min(count, maxOccurrences ?? int.MaxValue), 1, MaxCount);
        var dates = new List<DateOnly>();
        var due = First(rule, mode, startsOn, zone);
        while (due.IsSuccess && dates.Count < limit)
        {
            var day = CivilDate.Of(due.Value, zone);
            if (endsOn is { } ends && day > ends)
                break;
            dates.Add(day);
            due = Next(rule, mode, due.Value, zone);
        }
        return new RecurrencePreviewResult(dates, Warnings(rule, mode, startsOn, endsOn, dates));
    }

    /// <summary>La primera fecha desde <paramref name="startsOn"/>: la que muestra la vista previa y la que guarda la serie.</summary>
    public static Result<DateTime> FirstDueUtc(RecurrenceRule rule, RecurrenceMode mode, DateOnly startsOn) =>
        First(rule, mode, startsOn, ResolvedTimeZone.Resolve(rule.TimeZoneId));

    private static Result<DateTime> First(
        RecurrenceRule rule,
        RecurrenceMode mode,
        DateOnly startsOn,
        ResolvedTimeZone zone
    ) =>
        mode == RecurrenceMode.AfterCompletion
            ? Result.Success(LocalDay.EndUtc(startsOn, zone))
            : rule.WithoutInterval().NextAfter(LocalDay.EndUtc(startsOn.AddDays(-1), zone));

    private static Result<DateTime> Next(
        RecurrenceRule rule,
        RecurrenceMode mode,
        DateTime previousUtc,
        ResolvedTimeZone zone
    ) =>
        mode == RecurrenceMode.AfterCompletion && rule.PlainMonthlyInterval() is { } months
            ? Result.Success(TaskSeries.AddMonthsToCivilDay(previousUtc, months, zone))
            : rule.NextAfter(previousUtc);

    /// <summary>Los valores de una parte de la regla ("BYMONTHDAY" → ["15", "-1"]); vacío si no está.</summary>
    private static string[] PartValues(string rule, string name)
    {
        var part = rule.Split(';').FirstOrDefault(p => p.StartsWith(name + "=", StringComparison.Ordinal));
        return part is null ? [] : part[(name.Length + 1)..].Split(',');
    }

    private static List<string> Warnings(
        RecurrenceRule rule,
        RecurrenceMode mode,
        DateOnly startsOn,
        DateOnly? endsOn,
        IReadOnlyList<DateOnly> dates
    )
    {
        var warnings = new List<string>();
        var text = rule.Value.ToUpperInvariant();
        var months = PartValues(text, "BYMONTH");
        var days = PartValues(text, "BYMONTHDAY");
        if (months is ["2"] && days.Contains("29"))
            warnings.Add("February 29 only exists in leap years.");
        else if (
            days.Select(d => int.TryParse(d, out var n) ? n : 0).FirstOrDefault(n => n is >= 29 and <= 31)
            is > 0
                and var day
        )
            warnings.Add($"Months without day {day} are skipped.");
        if (endsOn is { } ends && ends < startsOn)
            warnings.Add("The series ends before it starts.");
        else if (dates.Count == 0)
            warnings.Add("This schedule produces no dates.");
        if (RecurrenceSummary.Describe(rule.Value, mode) == RecurrenceSummary.Custom)
            warnings.Add("This is a custom schedule. It can't be changed with the options above.");
        return warnings;
    }
}
