using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

public sealed class DueDateTests
{
    private static readonly DateTime AprilFifteenth = new(2026, 4, 15, 3, 59, 0, DateTimeKind.Utc);

    [Fact]
    public void A_due_date_keeps_both_the_instant_and_the_zone_it_was_written_in()
    {
        var result = DueDate.Create(AprilFifteenth, "America/New_York", isStatutory: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(AprilFifteenth, result.Value.DueAtUtc);
        Assert.Equal("America/New_York", result.Value.TimeZoneId);
        Assert.True(result.Value.IsStatutory);
    }

    /// <summary>
    /// Un <c>DateTime</c> con <c>Kind.Local</c> o <c>Unspecified</c> significa cosas distintas según
    /// dónde corra el proceso: aceptarlo mueve el vencimiento al desplegar en otra región.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void A_due_instant_that_is_not_utc_is_rejected(DateTimeKind kind)
    {
        var result = DueDate.Create(DateTime.SpecifyKind(AprilFifteenth, kind), "America/New_York", false);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Due.NotUtc", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Eastern Standard Time")] // id de Windows, no IANA
    [InlineData("Marte/Olympus_Mons")]
    public void A_time_zone_that_is_not_a_valid_iana_id_is_rejected(string? timeZoneId)
    {
        var result = DueDate.Create(AprilFifteenth, timeZoneId, false);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Due.TimeZoneInvalid", result.Error.Code);
    }

    [Fact]
    public void The_time_zone_id_is_trimmed()
    {
        var result = DueDate.Create(AprilFifteenth, "  America/New_York  ", false);

        Assert.True(result.IsSuccess);
        Assert.Equal("America/New_York", result.Value.TimeZoneId);
    }

    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");

    /// <summary>Una fecha sin hora vence al terminar ese día en la oficina (23:59:59 local), no a medianoche UTC.</summary>
    [Fact]
    public void A_civil_due_date_ends_at_the_last_second_of_that_day_in_the_office()
    {
        var result = DueDate.EndOfCivilDay(new DateOnly(2026, 11, 15), NewYork, isStatutory: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTime(2026, 11, 16, 4, 59, 59, DateTimeKind.Utc), result.Value.DueAtUtc);
        Assert.Equal(new DateOnly(2026, 11, 15), result.Value.DueOn);
        Assert.Equal("America/New_York", result.Value.TimeZoneId);
    }

    [Fact]
    public void The_end_of_the_day_follows_the_daylight_saving_change()
    {
        // 8-mar-2026: en Nueva York empieza el horario de verano (UTC-4 al final del día).
        var result = DueDate.EndOfCivilDay(new DateOnly(2026, 3, 8), NewYork, isStatutory: false);

        Assert.Equal(new DateTime(2026, 3, 9, 3, 59, 59, DateTimeKind.Utc), result.Value.DueAtUtc);
    }

    /// <summary>Regla de lectura P5: lo guardado a medianoche UTC es una fecha civil enviada como instante.</summary>
    [Fact]
    public void A_due_saved_at_midnight_utc_reads_as_that_civil_date()
    {
        var result = DueDate.Create(new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc), "America/New_York", false);

        Assert.Equal(new DateOnly(2026, 11, 15), result.Value.DueOn);
    }

    [Fact]
    public void A_due_with_a_real_time_reads_as_the_civil_date_in_its_zone()
    {
        // 03:59Z del 15-abr = 23:59 del 14-abr en Nueva York.
        var result = DueDate.Create(AprilFifteenth, "America/New_York", true);

        Assert.Equal(new DateOnly(2026, 4, 14), result.Value.DueOn);
    }
}
