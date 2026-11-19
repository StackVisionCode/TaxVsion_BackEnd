using BuildingBlocks.Contracts.Work;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Contracts;

/// <summary>Regla común de vencido/próximo del Work Profile: fecha de hoy de la oficina, sin reloj interno.</summary>
public sealed class WorkDueTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);

    [Theory]
    [InlineData(2026, 11, 17, true)]
    [InlineData(2026, 11, 18, false)]
    [InlineData(2026, 11, 19, false)]
    public void A_civil_due_date_is_overdue_only_before_the_office_today(int y, int m, int d, bool expected)
    {
        Assert.Equal(expected, WorkDue.IsOverdue(new DateOnly(y, m, d), Today));
    }

    [Theory]
    [InlineData(2026, 11, 17, false)] // vencida: no cuenta como próxima
    [InlineData(2026, 11, 18, true)] // hoy
    [InlineData(2026, 11, 24, true)] // séptimo día de la ventana
    [InlineData(2026, 11, 25, false)] // fuera de la ventana
    public void A_civil_due_date_is_due_soon_within_the_next_seven_office_days(int y, int m, int d, bool expected)
    {
        Assert.Equal(expected, WorkDue.IsDueSoon(new DateOnly(y, m, d), Today));
    }

    [Fact]
    public void The_due_soon_window_ends_seven_days_after_today_exclusive()
    {
        Assert.Equal(new DateOnly(2026, 11, 25), WorkDue.DueSoonEnd(Today));
    }

    [Fact]
    public void An_instant_is_overdue_when_it_is_before_now()
    {
        var now = new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);

        Assert.True(WorkDue.IsOverdue(now.AddMinutes(-1), now));
        Assert.False(WorkDue.IsOverdue(now, now));
        Assert.False(WorkDue.IsOverdue(now.AddMinutes(1), now));
    }

    [Fact]
    public void An_instant_is_due_soon_from_now_until_the_office_window_end()
    {
        var now = new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);
        // Inicio del 25-nov en la oficina (p. ej. America/New_York = 05:00 UTC).
        var windowEndUtc = new DateTime(2026, 11, 25, 5, 0, 0, DateTimeKind.Utc);

        Assert.False(WorkDue.IsDueSoon(now.AddMinutes(-1), now, windowEndUtc));
        Assert.True(WorkDue.IsDueSoon(now, now, windowEndUtc));
        Assert.True(WorkDue.IsDueSoon(windowEndUtc.AddTicks(-1), now, windowEndUtc));
        Assert.False(WorkDue.IsDueSoon(windowEndUtc, now, windowEndUtc));
    }

    [Fact]
    public void Kinds_are_stable_kebab_case_and_unique()
    {
        string[] expected =
        [
            "tasks",
            "client-requests",
            "form-requests",
            "appointments",
            "meetings",
            "signatures",
            "support-tickets",
            "clients-preparer",
            "clients-assigned",
        ];

        Assert.Equal(expected, WorkKinds.All);
        Assert.Equal("tasks", WorkKinds.Tasks);
        Assert.Equal("client-requests", WorkKinds.ClientRequests);
        Assert.Equal("form-requests", WorkKinds.FormRequests);
        Assert.Equal("appointments", WorkKinds.Appointments);
        Assert.Equal("meetings", WorkKinds.Meetings);
        Assert.Equal("signatures", WorkKinds.Signatures);
        Assert.Equal("support-tickets", WorkKinds.SupportTickets);
        Assert.Equal("clients-preparer", WorkKinds.ClientsPreparer);
        Assert.Equal("clients-assigned", WorkKinds.ClientsAssigned);
    }

    [Fact]
    public void Limits_match_the_approved_contract()
    {
        Assert.Equal(5, WorkLimits.TopItems);
        Assert.Equal(100, WorkLimits.BatchUsers);
    }

    [Fact]
    public void Contracts_keep_the_approved_shape()
    {
        var item = new WorkItem(
            Guid.NewGuid(),
            WorkKinds.Tasks,
            "Review W-2",
            null,
            null,
            Today,
            "open",
            "/task?taskId=1"
        );
        var summary = new WorkSummaryResponse("tasks", "planner", [new WorkCount(WorkKinds.Tasks, 3, 1, 2, 0)], [item]);
        var batch = new WorkBatchResponse([new WorkBatchRow(Guid.NewGuid(), 3, 1)]);
        var provider = new WorkProviderDescriptor("tasks", "planner", "/tasks/work/{userId}", "/tasks/work/summary");

        Assert.Equal(3, summary.Counts[0].Open);
        Assert.Equal(Today, summary.Top[0].DueDate);
        Assert.Single(batch.Rows);
        Assert.Contains("{userId}", provider.WorkPath, StringComparison.Ordinal);
    }
}
