using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Types;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

public sealed class AppointmentTypeTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_type_is_created_with_its_defaults()
    {
        var type = AppointmentType.Create(Tenant, "Consulta inicial", TimeSpan.FromMinutes(30), "#1a2b3c", Now);

        Assert.True(type.IsSuccess);
        Assert.Equal("#1A2B3C", type.Value.ColorHex);
        Assert.True(type.Value.IsActive);
        Assert.False(type.Value.BlocksOnConflict);
        Assert.Null(type.Value.DailyCap);
    }

    [Theory]
    [InlineData("1A2B3C")]
    [InlineData("#GGGGGG")]
    [InlineData("#1A2B3")]
    [InlineData(null)]
    public void An_invalid_color_is_rejected(string? color)
    {
        var type = AppointmentType.Create(Tenant, "Consulta", TimeSpan.FromMinutes(30), color, Now);

        Assert.True(type.IsFailure);
        Assert.Equal("Calendar.Type.ColorInvalid", type.Error.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60 * 9)]
    public void A_duration_out_of_range_is_rejected(int minutes)
    {
        var type = AppointmentType.Create(Tenant, "Consulta", TimeSpan.FromMinutes(minutes), "#1A2B3C", Now);

        Assert.True(type.IsFailure);
        Assert.Equal("Calendar.Type.DurationOutOfRange", type.Error.Code);
    }

    [Fact]
    public void A_daily_cap_of_zero_is_rejected()
    {
        var type = AppointmentType.Create(Tenant, "Entrega", TimeSpan.FromMinutes(15), "#1A2B3C", Now, dailyCap: 0);

        Assert.True(type.IsFailure);
        Assert.Equal("Calendar.Type.DailyCapOutOfRange", type.Error.Code);
    }

    [Fact]
    public void A_type_is_deactivated_and_never_deleted()
    {
        // Las citas pasadas apuntan a su tipo: borrarlo las dejaria sin explicacion.
        var type = AppointmentType.Create(Tenant, "Firma", TimeSpan.FromMinutes(30), "#1A2B3C", Now).Value;

        type.Deactivate();
        Assert.False(type.IsActive);

        type.Reactivate();
        Assert.True(type.IsActive);
    }
}

public sealed class AvailabilityTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_weekday_rule_applies_monday_to_friday_and_not_the_weekend()
    {
        var rule = AvailabilityRule
            .Create(
                Tenant,
                Preparer,
                DaysOfWeekMask.Weekdays,
                new TimeOnly(9, 0),
                new TimeOnly(17, 0),
                "America/New_York",
                Now
            )
            .Value;

        Assert.True(rule.AppliesTo(DayOfWeek.Monday));
        Assert.True(rule.AppliesTo(DayOfWeek.Friday));
        Assert.False(rule.AppliesTo(DayOfWeek.Saturday));
        Assert.False(rule.AppliesTo(DayOfWeek.Sunday));
    }

    [Fact]
    public void A_rule_that_ends_before_it_starts_is_rejected()
    {
        var rule = AvailabilityRule.Create(
            Tenant,
            Preparer,
            DaysOfWeekMask.Weekdays,
            new TimeOnly(17, 0),
            new TimeOnly(9, 0),
            "America/New_York",
            Now
        );

        Assert.True(rule.IsFailure);
        Assert.Equal("Calendar.Availability.EndBeforeStart", rule.Error.Code);
    }

    [Fact]
    public void A_rule_without_days_is_rejected()
    {
        var rule = AvailabilityRule.Create(
            Tenant,
            Preparer,
            DaysOfWeekMask.None,
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            "America/New_York",
            Now
        );

        Assert.True(rule.IsFailure);
        Assert.Equal("Calendar.Availability.NoDays", rule.Error.Code);
    }

    [Fact]
    public void The_working_hours_are_wall_clock_with_their_zone()
    {
        // Guardarlas en UTC correria el horario de atencion una hora al cambiar el horario y la
        // oficina abriria a las 8.
        var rule = AvailabilityRule
            .Create(
                Tenant,
                Preparer,
                DaysOfWeekMask.Weekdays,
                new TimeOnly(9, 0),
                new TimeOnly(17, 0),
                "America/New_York",
                Now
            )
            .Value;

        Assert.Equal(new TimeOnly(9, 0), rule.StartTime);
        Assert.Equal("America/New_York", rule.TimeZone.Id);
    }

    [Fact]
    public void A_blocked_time_must_be_utc_on_both_ends()
    {
        var block = BlockedTime.Create(
            Tenant,
            Preparer,
            new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2026, 3, 10, 17, 0, 0, DateTimeKind.Unspecified),
            "vacaciones",
            Now
        );

        Assert.True(block.IsFailure);
        Assert.Equal("Calendar.Timing.NotUtc", block.Error.Code);
    }

    [Fact]
    public void A_blocked_time_knows_what_it_overlaps()
    {
        var block = BlockedTime
            .Create(
                Tenant,
                Preparer,
                new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 10, 13, 0, 0, DateTimeKind.Utc),
                "almuerzo",
                Now
            )
            .Value;

        Assert.True(
            block.Overlaps(
                new DateTime(2026, 3, 10, 12, 30, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 10, 13, 30, 0, DateTimeKind.Utc)
            )
        );
        Assert.False(
            block.Overlaps(
                new DateTime(2026, 3, 10, 13, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc)
            )
        );
    }
}
