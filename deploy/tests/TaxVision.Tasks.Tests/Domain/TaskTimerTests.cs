using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

public sealed class TaskTimerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Beto = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Stopping_a_timer_adds_its_hours_to_the_task()
    {
        var task = NewTask();
        var timer = task.StartTimer(Ana, isBillable: true, Now).Value;

        task.StopTimer(timer.Id, Ana, Now.AddMinutes(90));

        Assert.Equal(1.5m, task.ActualHours);
        Assert.False(timer.IsRunning);
    }

    /// <summary>Mientras corre no se imputa: las horas se cuentan al parar el reloj.</summary>
    [Fact]
    public void A_running_timer_contributes_nothing_yet()
    {
        var task = NewTask();

        task.StartTimer(Ana, isBillable: true, Now);

        Assert.Equal(0m, task.ActualHours);
    }

    [Fact]
    public void The_same_person_cannot_open_two_timers_on_one_task()
    {
        var task = NewTask();
        task.StartTimer(Ana, isBillable: true, Now);

        var second = task.StartTimer(Ana, isBillable: true, Now.AddMinutes(5));

        Assert.Equal(TaskErrors.Timer.AlreadyRunning, second.Error);
    }

    /// <summary>Dos personas trabajando la misma tarea a la vez es normal en una revisión.</summary>
    [Fact]
    public void Two_people_can_have_their_own_timer_on_the_same_task()
    {
        var task = NewTask();
        task.StartTimer(Ana, isBillable: true, Now);

        var other = task.StartTimer(Beto, isBillable: false, Now);

        Assert.True(other.IsSuccess);
        Assert.Equal(2, task.Timers.Count);
    }

    [Fact]
    public void Only_the_person_who_opened_the_timer_can_stop_it()
    {
        var task = NewTask();
        var timer = task.StartTimer(Ana, isBillable: true, Now).Value;

        var stopped = task.StopTimer(timer.Id, Beto, Now.AddHours(1));

        Assert.Equal(TaskErrors.Timer.NotOwner, stopped.Error);
    }

    [Fact]
    public void Stopping_an_already_stopped_timer_is_rejected()
    {
        var task = NewTask();
        var timer = task.StartTimer(Ana, isBillable: true, Now).Value;
        task.StopTimer(timer.Id, Ana, Now.AddHours(1));

        var again = task.StopTimer(timer.Id, Ana, Now.AddHours(2));

        Assert.Equal(TaskErrors.Timer.NotRunning, again.Error);
        Assert.Equal(1m, task.ActualHours);
    }

    [Fact]
    public void A_closed_task_takes_no_new_timers()
    {
        var task = NewTask();
        task.Complete(Ana, Now);

        var started = task.StartTimer(Ana, isBillable: true, Now);

        Assert.Equal(TaskErrors.InvalidTransition(TaskItemStatus.Completed, "StartTimer"), started.Error);
    }

    /// <summary>
    /// Si no se pudiera, el tramo quedaría corriendo para siempre y las horas nunca cuadrarían.
    /// </summary>
    [Fact]
    public void A_timer_can_be_stopped_after_the_task_was_completed()
    {
        var task = NewTask();
        var timer = task.StartTimer(Ana, isBillable: true, Now).Value;
        task.Complete(Ana, Now.AddMinutes(30));

        var stopped = task.StopTimer(timer.Id, Ana, Now.AddMinutes(30));

        Assert.True(stopped.IsSuccess);
        Assert.Equal(0.5m, task.ActualHours);
    }

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                TenantId,
                Ana,
                TaskTitle.Create("Preparar 1040 de Pérez").Value,
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
