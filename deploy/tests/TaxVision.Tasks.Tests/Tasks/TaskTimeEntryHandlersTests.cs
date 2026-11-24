using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Timers.Commands;
using TaxVision.Tasks.Application.Timers.Queries;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>Los casos de uso del tiempo: el día de la oficina, los permisos y lo que se guarda.</summary>
public sealed class TaskTimeEntryHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Beto = Guid.NewGuid();

    [Fact]
    public async Task Logging_time_starts_the_entry_at_the_beginning_of_that_day_in_the_office()
    {
        var task = NewTask(Ana);
        var uow = new RecordingUnitOfWork();
        var zone = ResolvedTimeZone.Resolve("America/New_York");
        var yesterday = CivilDate.Of(DateTime.UtcNow, zone).AddDays(-1);

        var logged = await LogTaskTimeHandler.Handle(
            new LogTaskTimeCommand(TenantId, task.Id, Beto, yesterday, 45, true, " Reviewed W-2s "),
            new InMemoryTaskRepository(task),
            new FixedOfficeZone("America/New_York"),
            uow,
            default
        );

        Assert.True(logged.IsSuccess);
        Assert.Equal(LocalDay.StartUtc(yesterday, zone), logged.Value.StartedAtUtc);
        Assert.Equal((true, "Reviewed W-2s", Beto), (logged.Value.IsManual, logged.Value.Note, logged.Value.UserId));
        Assert.Equal(0.75m, task.ActualHours);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task A_future_day_in_the_office_is_rejected_and_nothing_is_saved()
    {
        var task = NewTask(Ana);
        var uow = new RecordingUnitOfWork();
        var tomorrow = CivilDate.Of(DateTime.UtcNow, ResolvedTimeZone.Resolve("America/New_York")).AddDays(1);

        var logged = await LogTaskTimeHandler.Handle(
            new LogTaskTimeCommand(TenantId, task.Id, Ana, tomorrow, 30, true, null),
            new InMemoryTaskRepository(task),
            new FixedOfficeZone("America/New_York"),
            uow,
            default
        );

        Assert.Equal(TaskErrors.Time.DateInFuture, logged.Error);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task Someone_else_corrects_an_entry_only_with_manage_all()
    {
        var task = NewTask(Ana);
        var entry = task.LogTime(Ana, Today(), 60, true, null, DateTime.UtcNow).Value;
        var repo = new InMemoryTaskRepository(task);

        var denied = await EditTaskTimeEntryHandler.Handle(
            new EditTaskTimeEntryCommand(TenantId, task.Id, entry.Id, Beto, false, 30, true, null),
            repo,
            new RecordingUnitOfWork(),
            default
        );
        var allowed = await RemoveTaskTimeEntryHandler.Handle(
            new RemoveTaskTimeEntryCommand(TenantId, task.Id, entry.Id, Beto, true),
            repo,
            new RecordingUnitOfWork(),
            default
        );

        Assert.Equal(TaskErrors.Time.NotOwner, denied.Error);
        Assert.True(allowed.IsSuccess);
        Assert.Equal(0m, task.ActualHours);
    }

    [Fact]
    public async Task The_estimate_follows_the_task_ownership_and_travels_in_whole_minutes()
    {
        var task = NewTask(Ana);
        var repo = new InMemoryTaskRepository(task);

        var outsider = await ChangeTaskEstimateHandler.Handle(
            new ChangeTaskEstimateCommand(TenantId, task.Id, Beto, false, 90),
            repo,
            new RecordingUnitOfWork(),
            default
        );
        var owner = await ChangeTaskEstimateHandler.Handle(
            new ChangeTaskEstimateCommand(TenantId, task.Id, Ana, false, 90),
            repo,
            new RecordingUnitOfWork(),
            default
        );
        var removed = await ChangeTaskEstimateHandler.Handle(
            new ChangeTaskEstimateCommand(TenantId, task.Id, Ana, false, null),
            repo,
            new RecordingUnitOfWork(),
            default
        );

        Assert.Equal(TaskErrors.Forbidden, outsider.Error);
        Assert.Equal(1.5m, owner.Value.EstimatedHours);
        Assert.Null(removed.Value.EstimatedHours);
    }

    [Fact]
    public async Task My_running_timers_list_only_mine()
    {
        var task = NewTask(Ana);
        var mine = task.StartTimer(Ana, true, DateTime.UtcNow).Value;
        task.StartTimer(Beto, true, DateTime.UtcNow);

        var running = await ListMyRunningTimersHandler.Handle(
            new ListMyRunningTimersQuery(TenantId, Ana),
            new InMemoryTaskRepository(task),
            default
        );

        Assert.Equal(mine.Id, Assert.Single(running).TimerId);
    }

    private static WorkDay Today()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new WorkDay(today, today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), today);
    }

    private static TaskItem NewTask(Guid owner) =>
        TaskItem
            .Create(
                TenantId,
                owner,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                owner,
                DateTime.UtcNow
            )
            .Value;
}
