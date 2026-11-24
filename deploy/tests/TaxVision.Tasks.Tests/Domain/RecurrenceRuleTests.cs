using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

public sealed class RecurrenceRuleTests
{
    private static readonly DateTime Seed = new(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_valid_rrule_is_accepted()
    {
        var rule = RecurrenceRule.Create("FREQ=MONTHLY;INTERVAL=3", "America/New_York");

        Assert.True(rule.IsSuccess);
        Assert.Equal("America/New_York", rule.Value.TimeZoneId);
    }

    /// <summary>
    /// <c>FrequencyType</c> de Ical.Net no tiene un valor «ninguna», así que un texto sin
    /// <c>FREQ=</c> se construiría con la primera frecuencia del enum en vez de fallar.
    /// </summary>
    [Theory]
    [InlineData("todos los meses")]
    [InlineData("INTERVAL=3")]
    [InlineData("")]
    public void Text_that_is_not_an_rrule_is_rejected(string value)
    {
        var rule = RecurrenceRule.Create(value, "America/New_York");

        Assert.True(rule.IsFailure);
    }

    [Fact]
    public void An_unknown_time_zone_is_rejected()
    {
        var rule = RecurrenceRule.Create("FREQ=DAILY", "Marte/Olympus");

        Assert.Equal(TaskErrors.Series.TimeZoneInvalid, rule.Error);
    }

    [Fact]
    public void The_next_occurrence_is_strictly_after_the_seed()
    {
        var rule = RecurrenceRule.Create("FREQ=MONTHLY;INTERVAL=3", "America/New_York").Value;

        var next = rule.NextAfter(Seed);

        Assert.Equal(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc), next.Value);
    }

    // La serie re-siembra la regla en cada paso, así que COUNT nunca se agotaba y un UNTIL ilegible se
    // ignoraba. El fin se pide aparte (fecha de fin o número de tareas).
    [Theory]
    [InlineData("FREQ=DAILY;COUNT=1")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=15;COUNT=12")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=15;UNTIL=20271231T235959Z")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=15;UNTIL=nope")]
    public void A_rule_that_carries_its_own_end_is_rejected(string value)
    {
        var rule = RecurrenceRule.Create(value, "America/New_York");

        Assert.True(rule.IsFailure);
        Assert.Equal("Task.Series.RuleEndNotSupported", rule.Error.Code);
    }

    // Una tarea por hora o por minuto no es una obligación de oficina, y el cálculo de fechas saltadas
    // recorría cientos de miles de pasos dentro de la petición.
    [Theory]
    [InlineData("FREQ=HOURLY")]
    [InlineData("FREQ=MINUTELY;INTERVAL=30")]
    [InlineData("FREQ=SECONDLY")]
    public void A_rule_more_frequent_than_daily_is_rejected(string value)
    {
        var rule = RecurrenceRule.Create(value, "America/New_York");

        Assert.True(rule.IsFailure);
        Assert.Equal("Task.Series.RuleFrequencyTooShort", rule.Error.Code);
    }

    [Fact]
    public void A_local_seed_is_rejected()
    {
        var rule = RecurrenceRule.Create("FREQ=DAILY", "America/New_York").Value;

        var next = rule.NextAfter(new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Local));

        Assert.Equal(TaskErrors.Series.SeedNotUtc, next.Error);
    }

    // Combinaciones que no existen en ningún año: aceptarlas dejaba una tarea que no se podía cerrar.
    [Theory]
    [InlineData("FREQ=YEARLY;BYMONTH=4;BYMONTHDAY=31")]
    [InlineData("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=30")]
    [InlineData("FREQ=MONTHLY;BYDAY=6MO")]
    public void A_rule_that_never_produces_a_date_is_rejected(string value)
    {
        var rule = RecurrenceRule.Create(value, "America/New_York");

        Assert.True(rule.IsFailure);
        Assert.Equal("Task.Series.RuleNeverOccurs", rule.Error.Code);
    }

    // Se saltan meses (RFC 5545), pero sí producen fechas: siguen siendo válidas.
    [Theory]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=31")]
    [InlineData("FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=31")]
    [InlineData("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=29")]
    public void A_rule_that_skips_some_months_is_still_accepted(string value)
    {
        Assert.True(RecurrenceRule.Create(value, "America/New_York").IsSuccess);
    }

    // Cada 4 años el 29 de febrero: desde 2001 nunca cae en bisiesto. Debe terminar, no lanzar.
    [Fact]
    public void A_rule_with_no_more_dates_from_this_seed_reports_no_further_occurrence()
    {
        var rule = RecurrenceRule.Create("FREQ=YEARLY;INTERVAL=4;BYMONTH=2;BYMONTHDAY=29", "America/New_York").Value;

        var next = rule.NextAfter(new DateTime(2001, 3, 1, 5, 0, 0, DateTimeKind.Utc));

        Assert.Equal(TaskErrors.Series.NoFurtherOccurrence, next.Error);
    }
}
