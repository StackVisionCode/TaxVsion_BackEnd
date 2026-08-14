using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Counters;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Counters;

public sealed class CounterReconcilerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_stale_blocker_count_is_brought_down_to_the_real_number()
    {
        var task = NewTask();
        task.RegisterBlockerAdded();
        task.RegisterBlockerAdded();
        var (reconciler, unitOfWork) = Build(task, blockers: (2, 0), subtasks: (0, 0));

        var fixedCount = await reconciler.ReconcileAsync(100, CancellationToken.None);

        Assert.Equal(1, fixedCount);
        Assert.False(task.IsBlocked);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_stale_subtask_count_is_corrected_too()
    {
        var task = NewTask();
        task.RegisterSubtaskOpened();
        task.RegisterSubtaskOpened();
        task.RegisterSubtaskOpened();
        var (reconciler, _) = Build(task, blockers: (0, 0), subtasks: (3, 1));

        await reconciler.ReconcileAsync(100, CancellationToken.None);

        Assert.Equal(1, task.OpenSubtaskCount);
    }

    [Fact]
    public async Task Correcting_one_counter_does_not_wipe_the_other()
    {
        var task = NewTask();
        task.RegisterBlockerAdded();
        task.RegisterSubtaskOpened();
        task.RegisterSubtaskOpened();
        var (reconciler, _) = Build(task, blockers: (1, 1), subtasks: (2, 0));

        await reconciler.ReconcileAsync(100, CancellationToken.None);

        Assert.Equal(1, task.OpenBlockerCount);
        Assert.Equal(0, task.OpenSubtaskCount);
    }

    [Fact]
    public async Task Nothing_out_of_sync_means_no_save()
    {
        var unitOfWork = new RecordingUnitOfWork();
        var reconciler = new CounterReconciler(
            new InMemoryTaskRepository(),
            new InMemoryTaskDependencyRepository(),
            unitOfWork,
            NullLogger<CounterReconciler>.Instance
        );

        var fixedCount = await reconciler.ReconcileAsync(100, CancellationToken.None);

        Assert.Equal(0, fixedCount);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private static (CounterReconciler Reconciler, RecordingUnitOfWork UnitOfWork) Build(
        TaskItem task,
        (int Stored, int Actual) blockers,
        (int Stored, int Actual) subtasks
    )
    {
        var dependencies = new InMemoryTaskDependencyRepository();
        dependencies.Mismatches.Add(
            new CounterMismatch(TenantId, task.Id, blockers.Stored, blockers.Actual, subtasks.Stored, subtasks.Actual)
        );

        var unitOfWork = new RecordingUnitOfWork();
        var reconciler = new CounterReconciler(
            new InMemoryTaskRepository(task),
            dependencies,
            unitOfWork,
            NullLogger<CounterReconciler>.Instance
        );
        return (reconciler, unitOfWork);
    }

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                TenantId,
                UserId,
                TaskTitle.Create("Preparar 1040 de Pérez").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                UserId,
                Now
            )
            .Value;
}
