using TaxVision.Calendar.Domain.Office;
using TaxVision.Calendar.Domain.Scheduling;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

public sealed class OfficeCalendarTests
{
    private const string NewYork = "America/New_York";

    private static DateTime Utc(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_preparers_hours_are_cut_to_the_office_hours_across_the_dst_change()
    {
        // El preparador 8–17 de lunes a viernes; la oficina abre 9–18 los lunes y no abre los viernes.
        // El 8 de marzo de 2026 empieza el horario de verano en Nueva York.
        var rules = new[]
        {
            new TimeSlot(Utc(3, 6, 13), Utc(3, 6, 22)), // viernes 6, 8–17 EST
            new TimeSlot(Utc(3, 9, 12), Utc(3, 9, 21)), // lunes 9, 8–17 EDT
        };
        var office = OfficeSchedule.Of(NewYork, [new OfficeHours(DayOfWeek.Monday, new(9, 0), new(18, 0))], []);

        var windows = OfficeCalendar.Restrict(rules, Utc(3, 5, 0), Utc(3, 11, 0), office);

        var monday = Assert.Single(windows);
        Assert.Equal(Utc(3, 9, 13), monday.StartUtc); // 9:00 EDT
        Assert.Equal(Utc(3, 9, 21), monday.EndUtc); // 17:00 EDT, el fin del preparador
    }

    [Fact]
    public void Closures_take_whole_days_out_including_yearly_ones_and_ranges()
    {
        var closures = new[]
        {
            new OfficeClosureSpan(new DateOnly(2025, 12, 25), null, "Christmas", RepeatsYearly: true),
            new OfficeClosureSpan(new DateOnly(2026, 11, 26), new DateOnly(2026, 11, 27), "Thanksgiving", false),
        };

        Assert.True(OfficeCalendar.IsClosed(new DateOnly(2026, 12, 25), closures, out var label));
        Assert.Equal("Christmas", label);
        Assert.True(OfficeCalendar.IsClosed(new DateOnly(2026, 11, 27), closures, out _));
        Assert.False(OfficeCalendar.IsClosed(new DateOnly(2026, 11, 28), closures, out _));
        Assert.False(OfficeCalendar.IsClosed(new DateOnly(2024, 12, 25), closures, out _));
    }

    [Fact]
    public void An_office_without_hours_only_removes_its_closed_days()
    {
        var rules = new[]
        {
            new TimeSlot(Utc(11, 25, 14), Utc(11, 25, 22)),
            new TimeSlot(Utc(11, 26, 14), Utc(11, 26, 22)),
        };
        var office = OfficeSchedule.Of(
            NewYork,
            [],
            [new OfficeClosureSpan(new DateOnly(2026, 11, 26), null, "Thanksgiving", false)]
        );

        var windows = OfficeCalendar.Restrict(rules, Utc(11, 25, 0), Utc(11, 28, 0), office);

        Assert.Equal([Utc(11, 25, 14)], windows.Select(w => w.StartUtc));
    }

    [Fact]
    public void Without_an_office_schedule_the_rules_stand_as_they_are()
    {
        var rules = new[] { new TimeSlot(Utc(11, 25, 14), Utc(11, 25, 22)) };

        Assert.Equal(rules, OfficeCalendar.Restrict(rules, Utc(11, 25, 0), Utc(11, 26, 0), office: null));
    }
}
