using TaxVision.Calendar.Domain.Office;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

public sealed class OfficeScheduleProjectionTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_older_profile_version_is_ignored()
    {
        var projection = OfficeScheduleProjection.Create(Guid.NewGuid(), Now);
        projection.ApplyProfile(3, [new OfficeHours(DayOfWeek.Monday, new(9, 0), new(17, 0))], [], Now);

        var applied = projection.ApplyProfile(2, [], [], Now);

        Assert.False(applied);
        Assert.Single(projection.Hours);
    }

    [Fact]
    public void The_zone_follows_the_latest_change_and_falls_back_to_the_preparers()
    {
        var projection = OfficeScheduleProjection.Create(Guid.NewGuid(), Now);
        Assert.Equal("America/Chicago", projection.ToSchedule("America/Chicago")!.ZoneId);

        projection.ApplyTimeZone("America/New_York", Now, Now);
        Assert.False(projection.ApplyTimeZone("America/Denver", Now.AddMinutes(-5), Now));

        Assert.Equal("America/New_York", projection.ToSchedule("America/Chicago")!.ZoneId);
    }

    [Fact]
    public void Hours_and_closures_survive_the_round_trip()
    {
        var projection = OfficeScheduleProjection.Create(Guid.NewGuid(), Now);
        projection.ApplyProfile(
            1,
            [new OfficeHours(DayOfWeek.Friday, new(8, 30), new(12, 0))],
            [new OfficeClosureSpan(new DateOnly(2026, 12, 25), null, "Christmas", true)],
            Now
        );

        var hours = Assert.Single(projection.Hours);
        Assert.Equal(new TimeOnly(8, 30), hours.Opens);
        var closure = Assert.Single(projection.Closures);
        Assert.True(closure.RepeatsYearly);
        Assert.Equal("Christmas", closure.Label);
    }
}
