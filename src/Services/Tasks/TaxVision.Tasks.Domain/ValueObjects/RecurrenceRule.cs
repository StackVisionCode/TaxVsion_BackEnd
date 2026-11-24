using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
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

        if (!TryParse(rule, out var pattern, out var reason))
            return Result.Failure<RecurrenceRule>(TaskErrors.Series.RuleUnreadable(reason));

        if (pattern.Frequency is FrequencyType.Secondly or FrequencyType.Minutely or FrequencyType.Hourly)
            return Result.Failure<RecurrenceRule>(TaskErrors.Series.RuleFrequencyTooShort);

        // Por texto y no por el patrón: Ical.Net descarta en silencio un UNTIL que no sabe leer.
        if (
            rule.Contains("COUNT=", StringComparison.OrdinalIgnoreCase)
            || rule.Contains("UNTIL=", StringComparison.OrdinalIgnoreCase)
        )
            return Result.Failure<RecurrenceRule>(TaskErrors.Series.RuleEndNotSupported);

        if (!ProducesAnyDate(pattern, normalizedTimeZoneId))
            return Result.Failure<RecurrenceRule>(TaskErrors.Series.RuleNeverOccurs);

        return Result.Success(new RecurrenceRule(rule, normalizedTimeZoneId));
    }

    /// <summary>
    /// La misma regla en otra zona (cambio de zona de la oficina). No vuelve a validar: una serie guardada
    /// antes de una validación nueva tiene que poder seguir moviéndose de zona.
    /// </summary>
    internal RecurrenceRule InTimeZone(string timeZoneId) => new(Value, timeZoneId);

    /// <summary>Una fecha al año (o cada N años): el año fiscal de cada ocurrencia avanza con su vencimiento.</summary>
    internal bool IsAnnual =>
        TryParse(Value, out var pattern) && pattern.Frequency == FrequencyType.Yearly && pattern.ByMonth.Count <= 1;

    /// <summary>La regla fija sus días; sin BY* los toma del ancla y cualquier ancla es fecha de la regla.</summary>
    internal bool FixesItsDays =>
        Value.Split(';').Any(part => part.StartsWith("BY", StringComparison.OrdinalIgnoreCase));

    /// <summary>La misma regla sin INTERVAL: la primera fecha de la vista previa se busca así.</summary>
    internal RecurrenceRule WithoutInterval() =>
        new(
            string.Join(
                ';',
                Value
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Where(part => !part.StartsWith("INTERVAL=", StringComparison.OrdinalIgnoreCase))
            ),
            TimeZoneId
        );

    /// <summary>
    /// "Cada N meses" sin día fijo (FREQ=MONTHLY y como mucho INTERVAL): el intervalo, o null si la regla fija días.
    /// </summary>
    public int? PlainMonthlyInterval()
    {
        if (!TryParse(Value, out var pattern) || pattern.Frequency != FrequencyType.Monthly)
            return null;
        var plain =
            pattern.ByMonthDay.Count == 0
            && pattern.ByDay.Count == 0
            && pattern.ByMonth.Count == 0
            && pattern.BySetPosition.Count == 0;
        return plain ? Math.Max(1, pattern.Interval) : null;
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

        try
        {
            foreach (var occurrence in series.GetOccurrences(seed))
            {
                var startUtc = occurrence.Period.StartTime.AsUtc;
                if (startUtc > seedUtc)
                    return Result.Success(startUtc);
            }
        }
        catch (EvaluationOutOfRangeException)
        {
            // Desde esta semilla la regla ya no cae en ninguna fecha (p. ej. 29-feb cada 4 años desde 2001):
            // es un fin, no un error que impida cerrar la tarea.
        }

        return Result.Failure<DateTime>(TaskErrors.Series.NoFurtherOccurrence);
    }

    /// <summary>
    /// Ical.Net lanza al buscar una fecha que no existe en ningún año (31 de abril). Se prueba desde el
    /// año 2000 y se piden solo dos ocurrencias: una regla válida sin fin no se recorre entera.
    /// </summary>
    private static bool ProducesAnyDate(RecurrencePattern pattern, string timeZoneId)
    {
        var start = new CalDateTime(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), timeZoneId);
        var series = new CalendarEvent { Start = start, RecurrenceRules = [pattern] };
        try
        {
            _ = series.GetOccurrences(start).Take(2).Count();
            return true;
        }
        catch (EvaluationOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// El <c>FREQ=</c> se exige aparte: <c>FrequencyType</c> no tiene un valor «ninguna», así que un
    /// texto sin frecuencia se construiría con la primera del enum en vez de fallar.
    /// </summary>
    private static bool TryParse(string rule, out RecurrencePattern pattern) => TryParse(rule, out pattern, out _);

    /// <summary>El motivo va al usuario: la primera línea de lo que dijo el lector, acotada.</summary>
    private static bool TryParse(string rule, out RecurrencePattern pattern, out string reason)
    {
        pattern = null!;
        reason = string.Empty;
        if (!rule.Contains("FREQ=", StringComparison.OrdinalIgnoreCase))
        {
            reason = "it needs FREQ, for example FREQ=MONTHLY.";
            return false;
        }

        try
        {
            pattern = new RecurrencePattern(rule);
            return true;
        }
        catch (Exception ex)
        {
            var line = ex.Message.Split(Environment.NewLine.ToCharArray())[0].Trim();
            reason = line.Length > 160 ? line[..160] : line;
            return false;
        }
    }
}
