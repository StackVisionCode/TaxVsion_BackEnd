using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class WeeklyBusinessHoursTests
{
    [Fact]
    public void Orders_days_from_monday_to_sunday()
    {
        var result = WeeklyBusinessHours.Create([
            new("Sunday", "10:00", "12:00"),
            new("wednesday", "09:00", "17:00"),
            new("Monday", "09:00", "17:30"),
        ]);

        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Sunday],
            result.Value.Days.Select(day => day.Day)
        );
        Assert.Equal(new TimeOnly(17, 30), result.Value.Days[0].Closes);
    }

    [Fact]
    public void Rejects_a_repeated_day()
    {
        var result = WeeklyBusinessHours.Create([new("Friday", "09:00", "12:00"), new("friday", "13:00", "17:00")]);

        Assert.Equal(WeeklyBusinessHours.DuplicatedDay, result.Error);
    }

    [Fact]
    public void Rejects_closing_before_opening()
    {
        var result = WeeklyBusinessHours.Create([new BusinessHoursDraft("Friday", "18:00", "09:00")]);

        Assert.Equal(DailyOpeningHours.ClosesBeforeOpening, result.Error);
    }

    [Fact]
    public void Matches_compares_content_not_references()
    {
        var first = WeeklyBusinessHours.Create([new BusinessHoursDraft("Monday", "09:00", "17:00")]).Value;
        var second = WeeklyBusinessHours.Create([new BusinessHoursDraft("monday", "09:00", "17:00")]).Value;

        Assert.True(second.Matches(first.Days));
        Assert.False(WeeklyBusinessHours.None.Matches(first.Days));
    }
}
