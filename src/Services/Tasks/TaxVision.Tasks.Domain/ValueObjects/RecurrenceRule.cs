using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.ValueObjects;

/// <summary>
/// RRULE de RFC 5545 en un solo campo, más la zona en que se interpreta. Ical.Net valida en la
/// fábrica: si el texto no es un RRULE legal, no se llega a construir el VO.
/// </summary>
public sealed record RecurrenceRule
{
    public const int MaxLength = 500;

    public string Value { get; }
    public string TimeZoneId { get; }

    private RecurrenceRule(string value, string timeZoneId)
    {
        Value = value;
        TimeZoneId = timeZoneId;
    }

    public static Result<RecurrenceRule> Create(string? value, string? timeZoneId)
    {
        var rule = value?.Trim();
        if (string.IsNullOrEmpty(rule))
            return Result.Failure<RecurrenceRule>(TaskErrors.Series.RuleEmpty);

        if (rule.Length > MaxLength)
            return Result.Failure<RecurrenceRule>(TaskErrors.Series.RuleTooLong);

        if (!IanaTimeZone.TryNormalize(timeZoneId, out var normalizedTimeZoneId))
            return Result.Failure<RecurrenceRule>(TaskErrors.Series.TimeZoneInvalid);

        if (!TryParse(rule, out _))
            return Result.Failure<RecurrenceRule>(TaskErrors.Series.RuleInvalid);

        return Result.Success(new RecurrenceRule(rule, normalizedTimeZoneId));
    }

    /// <summary>
    /// La primera ocurrencia <b>estrictamente posterior</b> a la semilla. Quién es la semilla es lo
    /// único que separa a los dos modos de recurrencia.
    /// </summary>
    public Result<DateTime> NextAfter(DateTime seedUtc)
    {
        if (seedUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<DateTime>(TaskErrors.Series.SeedNotUtc);

        if (!TryParse(Value, out var pattern))
            return Result.Failure<DateTime>(TaskErrors.Series.RuleInvalid);

        if (!IanaTimeZone.TryFindTimeZone(TimeZoneId, out var timeZone))
            return Result.Failure<DateTime>(TaskErrors.Series.TimeZoneInvalid);

        // CalDateTime lee el DateTime como hora LOCAL de la zona que se le pasa, no como UTC que hay
        // que convertir. Pasarle el instante UTC crudo corre la serie entera por el offset de la zona.
        var seedLocal = TimeZoneInfo.ConvertTimeFromUtc(seedUtc, timeZone);
        var seed = new CalDateTime(seedLocal, TimeZoneId);
        var series = new CalendarEvent { Start = seed, RecurrenceRules = [pattern] };

        foreach (var occurrence in series.GetOccurrences(seed))
        {
            var startUtc = occurrence.Period.StartTime.AsUtc;
            if (startUtc > seedUtc)
                return Result.Success(startUtc);
        }

        return Result.Failure<DateTime>(TaskErrors.Series.NoFurtherOccurrence);
    }

    /// <summary>
    /// El <c>FREQ=</c> se exige aparte: <c>FrequencyType</c> no tiene un valor «ninguna», así que un
    /// texto sin frecuencia se construiría con la primera del enum en vez de fallar.
    /// </summary>
    private static bool TryParse(string rule, out RecurrencePattern pattern)
    {
        pattern = null!;
        if (!rule.Contains("FREQ=", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            pattern = new RecurrencePattern(rule);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
