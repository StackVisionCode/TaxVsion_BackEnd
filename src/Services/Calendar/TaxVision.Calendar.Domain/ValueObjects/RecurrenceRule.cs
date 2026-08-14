using System.Globalization;
using BuildingBlocks.Results;
using Ical.Net.DataTypes;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>
/// El RRULE de RFC 5545 en un solo campo. Ical.Net valida en la fábrica: si el texto no es un RRULE
/// legal, no se llega a construir el VO.
///
/// <para>
/// No lleva zona horaria: vive en <see cref="EventTiming.TimeZone"/>. Duplicarla aca crea dos fuentes
/// para el mismo dato. La regla dice cada cuanto se repite; cuando y donde lo dice el timing.
/// </para>
/// </summary>
public sealed record RecurrenceRule
{
    public const int MaxLength = 500;

    public string Value { get; }

    private RecurrenceRule(string value) => Value = value;

    /// <summary>
    /// Si la serie termina alguna vez. Una sin <c>UNTIL</c> ni <c>COUNT</c> no vence nunca, y eso
    /// decide qué puede purgar la retención: lo que no termina no se puede dar por viejo.
    /// </summary>
    public bool HasEnd =>
        Value.Contains("UNTIL=", StringComparison.OrdinalIgnoreCase)
        || Value.Contains("COUNT=", StringComparison.OrdinalIgnoreCase);

    public static Result<RecurrenceRule> Create(string? value)
    {
        var rule = value?.Trim();

        if (string.IsNullOrEmpty(rule))
            return Result.Failure<RecurrenceRule>(RecurrenceErrors.RuleEmpty);

        if (rule.Length > MaxLength)
            return Result.Failure<RecurrenceRule>(RecurrenceErrors.RuleTooLong);

        return TryParse(rule, out _)
            ? Result.Success(new RecurrenceRule(rule))
            : Result.Failure<RecurrenceRule>(RecurrenceErrors.RuleInvalid);
    }

    /// <summary>
    /// La misma regla, terminada en <paramref name="untilUtc"/>: es lo que necesita la mitad vieja al
    /// partir una serie.
    ///
    /// <para>
    /// <c>UNTIL</c> va siempre en UTC, lo exige el RFC. Y si la regla traia <c>COUNT</c> hay que
    /// quitarlo: el RFC prohibe los dos a la vez, y dejarlos convierte el RRULE en texto que cada
    /// parser interpreta a su manera.
    /// </para>
    /// </summary>
    public Result<RecurrenceRule> EndingAt(DateTime untilUtc)
    {
        if (untilUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<RecurrenceRule>(RecurrenceErrors.UntilNotUtc);

        var rebuilt = new List<string>();

        foreach (var part in Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (
                part.StartsWith("UNTIL=", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("COUNT=", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            rebuilt.Add(part);
        }

        rebuilt.Add("UNTIL=" + untilUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));

        return Create(string.Join(';', rebuilt));
    }

    internal bool TryBuildPattern(out RecurrencePattern pattern) => TryParse(Value, out pattern);

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

    public override string ToString() => Value;
}
