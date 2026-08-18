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

    /// <summary>Con COUNT agotado la regla no tiene más fechas: es un fin, no un error de datos.</summary>
    [Fact]
    public void An_exhausted_rule_reports_no_further_occurrence()
    {
        var rule = RecurrenceRule.Create("FREQ=DAILY;COUNT=1", "America/New_York").Value;

        var next = rule.NextAfter(Seed);

        Assert.Equal(TaskErrors.Series.NoFurtherOccurrence, next.Error);
    }

    [Fact]
    public void A_local_seed_is_rejected()
    {
        var rule = RecurrenceRule.Create("FREQ=DAILY", "America/New_York").Value;

        var next = rule.NextAfter(new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Local));

        Assert.Equal(TaskErrors.Series.SeedNotUtc, next.Error);
    }
}
