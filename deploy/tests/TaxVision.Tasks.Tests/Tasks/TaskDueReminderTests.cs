using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Tasks;

public sealed class TaskDueReminderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Assignee = Guid.NewGuid();

    /// <summary>Un vencimiento sin hora avisa a las 9:00 de la oficina del mismo día, no a las 22:59.</summary>
    [Fact]
    public void A_civil_due_reminds_at_nine_in_the_morning_of_that_day_in_the_office()
    {
        var due = DueDate
            .EndOfCivilDay(new DateOnly(2026, 11, 15), ResolvedTimeZone.Resolve("America/New_York"), false)
            .Value;

        var request = TaskDueReminder.RequestFor(NewTask(due), Assignee, 0, "corr")!;

        // 9:00 en Nueva York el 15-nov = 14:00Z.
        var fireAtUtc = request.AnchorAtUtc!.Value.AddMinutes(-(request.LeadMinutes ?? 0));
        Assert.InRange(
            fireAtUtc,
            new DateTime(2026, 11, 15, 13, 59, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 15, 14, 0, 0, DateTimeKind.Utc)
        );
    }

    [Fact]
    public void A_due_with_a_real_time_keeps_reminding_an_hour_before()
    {
        var due = DueDate
            .Create(new DateTime(2026, 11, 15, 18, 30, 0, DateTimeKind.Utc), "America/New_York", false)
            .Value;

        var request = TaskDueReminder.RequestFor(NewTask(due), Assignee, 0, "corr")!;

        Assert.Equal(60, request.LeadMinutes);
    }

    private static TaskItem NewTask(DueDate due) =>
        TaskItem
            .Create(
                TenantId,
                Guid.NewGuid(),
                TaskTitle.Create("Preparar 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                due,
                null,
                Assignee,
                new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)
            )
            .Value;
}
