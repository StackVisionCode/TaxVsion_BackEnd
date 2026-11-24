using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

/// <summary>Tiempo a mano, corrección, borrado y estimación: el total siempre es la suma de los registros cerrados.</summary>
public sealed class TaskTimeEntryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Beto = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 23, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 23);

    private static WorkDay Day(DateOnly date) =>
        new(date, date.ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc), Today);

    [Fact]
    public void Logging_time_adds_a_closed_manual_entry_and_its_hours()
    {
        var task = NewTask();

        var entry = task.LogTime(Ana, Day(Today.AddDays(-1)), 45, isBillable: false, "Reviewed W-2s", Now).Value;

        Assert.True(entry.IsManual);
        Assert.False(entry.IsRunning);
        Assert.False(entry.IsBillable);
        Assert.Equal("Reviewed W-2s", entry.Note);
        Assert.Equal(45, (int)(entry.StoppedAtUtc!.Value - entry.StartedAtUtc).TotalMinutes);
        Assert.Equal(0.75m, task.ActualHours);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(24 * 60 + 1)]
    public void A_duration_of_zero_or_over_a_day_is_rejected(int minutes)
    {
        var task = NewTask();

        var logged = task.LogTime(Ana, Day(Today), minutes, true, null, Now);

        Assert.Equal(TaskErrors.Time.DurationInvalid, logged.Error);
        Assert.Equal(0m, task.ActualHours);
    }

    [Fact]
    public void A_future_date_is_rejected()
    {
        var logged = NewTask().LogTime(Ana, Day(Today.AddDays(1)), 30, true, null, Now);

        Assert.Equal(TaskErrors.Time.DateInFuture, logged.Error);
    }

    [Fact]
    public void A_completed_task_accepts_time_but_a_cancelled_one_does_not()
    {
        var completed = NewTask();
        completed.Complete(Ana, Now);
        var cancelled = NewTask();
        cancelled.Cancel("Not needed", Ana, Now);

        Assert.True(completed.LogTime(Ana, Day(Today), 30, true, null, Now).IsSuccess);
        Assert.Equal(TaskErrors.Time.TaskCancelled, cancelled.LogTime(Ana, Day(Today), 30, true, null, Now).Error);
    }

    [Fact]
    public void A_note_over_200_characters_is_rejected()
    {
        var logged = NewTask().LogTime(Ana, Day(Today), 30, true, new string('x', 201), Now);

        Assert.Equal(TaskErrors.Time.NoteTooLong, logged.Error);
    }

    [Fact]
    public void Editing_an_entry_adjusts_the_total_by_the_difference_and_keeps_who_changed_it()
    {
        var task = NewTask();
        var entry = task.LogTime(Ana, Day(Today), 60, true, null, Now).Value;
        task.LogTime(Beto, Day(Today), 30, true, null, Now);

        var edited = task.EditTimeEntry(entry.Id, Ana, false, 90, false, "Corrected", Now.AddHours(1));

        Assert.True(edited.IsSuccess);
        Assert.Equal(2m, task.ActualHours);
        Assert.Equal((false, "Corrected", Ana), (entry.IsBillable, entry.Note, entry.UpdatedByUserId!.Value));
        Assert.Equal(Now.AddHours(1), entry.UpdatedAtUtc);
    }

    [Fact]
    public void Only_the_author_edits_or_removes_an_entry_unless_they_manage_everything()
    {
        var task = NewTask();
        var entry = task.LogTime(Ana, Day(Today), 60, true, null, Now).Value;

        Assert.Equal(TaskErrors.Time.NotOwner, task.EditTimeEntry(entry.Id, Beto, false, 30, true, null, Now).Error);
        Assert.Equal(TaskErrors.Time.NotOwner, task.RemoveTimeEntry(entry.Id, Beto, false, Now).Error);
        Assert.True(task.EditTimeEntry(entry.Id, Beto, true, 30, true, null, Now).IsSuccess);
    }

    [Fact]
    public void A_running_timer_is_stopped_before_it_can_be_edited_or_removed()
    {
        var task = NewTask();
        var running = task.StartTimer(Ana, true, Now).Value;

        Assert.Equal(
            TaskErrors.Time.EntryRunning,
            task.EditTimeEntry(running.Id, Ana, false, 30, true, null, Now).Error
        );
        Assert.Equal(TaskErrors.Time.EntryRunning, task.RemoveTimeEntry(running.Id, Ana, false, Now).Error);
    }

    [Fact]
    public void Removing_an_entry_takes_its_hours_out_of_the_total()
    {
        var task = NewTask();
        var keep = task.LogTime(Ana, Day(Today), 30, true, null, Now).Value;
        var drop = task.LogTime(Ana, Day(Today), 45, true, null, Now).Value;

        Assert.True(task.RemoveTimeEntry(drop.Id, Ana, false, Now).IsSuccess);

        Assert.Equal(0.5m, task.ActualHours);
        Assert.Equal([keep.Id], task.Timers.Select(t => t.Id));
    }

    [Fact]
    public void An_unknown_entry_is_not_found()
    {
        Assert.Equal(TaskErrors.Timer.NotFound, NewTask().RemoveTimeEntry(Guid.NewGuid(), Ana, true, Now).Error);
    }

    [Fact]
    public void The_estimate_can_be_changed_and_removed()
    {
        var task = NewTask();

        task.ChangeEstimate(EstimatedHours.Create(3m).Value);
        Assert.Equal(3m, task.Estimated!.Value);

        task.ChangeEstimate(null);
        Assert.Null(task.Estimated);
    }

    [Fact]
    public void Correcting_or_removing_an_entry_is_announced_for_the_history()
    {
        var task = NewTask();
        var entry = task.LogTime(Ana, Day(Today), 90, true, null, Now).Value;
        task.ClearDomainEvents();

        task.EditTimeEntry(entry.Id, Beto, true, 60, true, null, Now);
        task.RemoveTimeEntry(entry.Id, Beto, true, Now.AddMinutes(1));

        var changes = task.DomainEvents.OfType<TaskTimeEntryChangedDomainEvent>().ToList();
        Assert.Equal(
            (1.5m, (decimal?)1m, Beto, Ana),
            (changes[0].PreviousHours, changes[0].Hours, changes[0].ByUserId, changes[0].EntryUserId)
        );
        Assert.Equal((1m, (decimal?)null), (changes[1].PreviousHours, changes[1].Hours));
    }

    [Fact]
    public void A_dependency_change_is_announced_for_the_history()
    {
        var task = NewTask();
        var blocker = Guid.NewGuid();
        task.ClearDomainEvents();

        task.RecordDependencyChange(blocker, added: true, Ana, Now);

        var change = Assert.Single(task.DomainEvents.OfType<TaskDependencyChangedDomainEvent>());
        Assert.Equal((blocker, true, Ana), (change.DependsOnTaskId, change.Added, change.ByUserId));
    }

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                TenantId,
                Ana,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                Ana,
                Now
            )
            .Value;
}
