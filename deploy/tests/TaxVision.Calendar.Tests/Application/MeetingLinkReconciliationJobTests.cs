using TaxVision.Calendar.Infrastructure.Jobs;
using Xunit;
using static TaxVision.Calendar.Tests.Application.ReminderTargetsTestData;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>CAL-10: la sala que se vuelve a pedir para una serie lleva su primera ocurrencia, igual que la cita nueva.</summary>
public sealed class MeetingLinkReconciliationJobTests
{
    [Fact]
    public void Rebuild_Series_RequestsTheRoomAtTheFirstOccurrence()
    {
        var monday = Next(DayOfWeek.Monday);
        var series = Weekly(Guid.NewGuid(), Guid.NewGuid(), monday, leadMinutes: null);
        var expected = TimeZoneInfo.ConvertTimeToUtc(
            monday.ToDateTime(TenAm),
            TimeZoneInfo.FindSystemTimeZoneById(NewYork)
        );

        var requested = MeetingLinkReconciliationJob.Rebuild(series, "corr");

        Assert.Equal(DateTime.SpecifyKind(expected, DateTimeKind.Utc), requested.StartUtc);
    }

    /// <summary>
    /// P5: una serie que ya empezó pide la sala para su próxima ocurrencia; con la primera, ya pasada,
    /// Communication la expiraba en el siguiente barrido.
    /// </summary>
    [Fact]
    public void Rebuild_SeriesAlreadyStarted_RequestsTheRoomAtTheNextOccurrence()
    {
        var mondayThreeWeeksAgo = Next(DayOfWeek.Monday).AddDays(-21);
        var series = Weekly(Guid.NewGuid(), Guid.NewGuid(), mondayThreeWeeksAgo, leadMinutes: null);

        var requested = MeetingLinkReconciliationJob.Rebuild(series, "corr");

        Assert.NotNull(requested.StartUtc);
        Assert.True(requested.StartUtc > DateTime.UtcNow);
        Assert.True(requested.StartUtc <= DateTime.UtcNow.AddDays(7));
    }
}
