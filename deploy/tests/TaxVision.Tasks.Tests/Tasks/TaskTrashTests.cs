using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Tasks.Application.Dependencies;
using TaxVision.Tasks.Application.Hierarchy;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;
using TaxVision.Tasks.Tests.Templates;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// Papelera de 30 días (D-25): borrar manda la tarea y su rama a la papelera y cierra sus avisos; Restore la devuelve
/// con sus contadores. Una ocurrencia abierta que la serie ya reemplazó no se restaura.
/// </summary>
public sealed class TaskTrashTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public async Task Deleting_moves_the_branch_to_the_trash_and_closes_every_reminder()
    {
        var parent = NewTask();
        var root = Sub(parent, "Collect W-2");
        var child = Sub(root, "Call employer");
        var fixture = new Fixture(parent, root, child);

        var result = await fixture.DeleteAsync(root);

        Assert.True(result.IsSuccess);
        Assert.All(new[] { root, child }, t => Assert.Equal((true, root.Id), (t.IsInTrash, t.TrashRootId)));
        Assert.False(parent.IsInTrash);
        Assert.Equal((0, 0), (parent.SubtaskCount, parent.OpenSubtaskCount));
        var closed = fixture.Bus.Published.OfType<ReminderTargetClosedIntegrationEvent>().ToList();
        foreach (var id in new[] { root.Id, child.Id })
        {
            Assert.Contains(closed, c => c.TargetId == id && c.Category == TaskReminderContracts.Category);
            Assert.Contains(
                closed,
                c => c.TargetId == id && c.Category == TaskReminderContracts.ClientResponseCategory
            );
        }
    }

    [Fact]
    public async Task Restore_brings_the_whole_branch_back_with_its_parent_counters()
    {
        var parent = NewTask();
        var root = Sub(parent, "Collect W-2");
        var child = Sub(root, "Call employer");
        var fixture = new Fixture(parent, root, child);
        await fixture.DeleteAsync(root);

        var restored = await fixture.RestoreAsync(root);

        Assert.True(restored.IsSuccess);
        Assert.All(new[] { root, child }, t => Assert.Equal((false, (Guid?)null), (t.IsInTrash, t.TrashRootId)));
        Assert.Equal((1, 1), (parent.SubtaskCount, parent.OpenSubtaskCount));
    }

    [Fact]
    public async Task A_subtask_whose_parent_is_in_the_trash_is_restored_with_it_not_alone()
    {
        var parent = NewTask();
        var child = Sub(parent, "Collect W-2");
        var fixture = new Fixture(parent, child);
        await fixture.DeleteAsync(parent);

        var alone = await fixture.RestoreAsync(child);

        Assert.Equal(TaskErrors.Trash.NotInTrash, alone.Error);
    }

    [Fact]
    public async Task An_open_occurrence_the_series_already_replaced_is_not_restored()
    {
        var task = NewTask();
        var fixture = new Fixture(task);
        var series = SeriesOf(task);
        fixture.Series.Add(series);
        // Borrar la ocurrencia abierta libera la serie y siembra la siguiente.
        await fixture.DeleteAsync(task);
        Assert.Equal(2, series.GeneratedCount);

        var restored = await fixture.RestoreAsync(task);

        Assert.Equal(TaskErrors.Series.OccurrenceSuperseded, restored.Error);
        Assert.True(task.IsInTrash);
    }

    private static TaskSeries SeriesOf(TaskItem task)
    {
        var series = TaskSeries
            .Create(
                TenantId,
                Owner,
                RecurrenceRule.Create("FREQ=MONTHLY;INTERVAL=1", "America/New_York").Value,
                RecurrenceMode.FixedSchedule,
                new TaskItemBlueprint
                {
                    Title = TaskTitle.Create("Payroll").Value,
                    Priority = TaskPriority.Normal,
                    Reference = TaskReference.None,
                    AssigneeUserId = Owner,
                    IsStatutory = false,
                },
                DateTime.UtcNow.AddDays(3),
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
        task.AttachToSeries(series.Id, 1);
        series.RegisterMaterialized(task.Id, series.PlanNextOccurrence(null, null, DateTime.UtcNow).Value);
        return series;
    }

    private static TaskItem Sub(TaskItem parent, string title) =>
        TaskItem
            .CreateSubtask(
                parent,
                Owner,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                Owner,
                DateTime.UtcNow
            )
            .Value;

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                Owner,
                DateTime.UtcNow
            )
            .Value;

    private sealed class Fixture(params TaskItem[] seed)
    {
        public InMemoryTaskRepository Tasks { get; } = new(seed);
        public InMemoryTaskSeriesRepository Series { get; } = new();
        public FakeMessageBus Bus { get; } = new();
        private readonly InMemoryTaskDependencyRepository _dependencies = new();

        private TaskSeriesMaterializer Materializer => new(Tasks, Series, new InMemoryTaskTemplateRepository());

        public Task<BuildingBlocks.Results.Result> DeleteAsync(TaskItem task) =>
            DeleteTaskHandler.Handle(
                new DeleteTaskCommand(TenantId, task.Id, Owner, true),
                Tasks,
                new TaskHierarchyService(Tasks, _dependencies),
                new TaskUnblockingService(Tasks, _dependencies, new NoOpCorrelationContext()),
                Materializer,
                new RecordingUnitOfWork(),
                Bus,
                new NoOpCorrelationContext(),
                CancellationToken.None
            );

        public Task<BuildingBlocks.Results.Result> RestoreAsync(TaskItem task) =>
            RestoreTaskHandler.Handle(
                new RestoreTaskCommand(TenantId, task.Id, Owner, true),
                Tasks,
                Series,
                new RecordingUnitOfWork(),
                CancellationToken.None
            );
    }
}
