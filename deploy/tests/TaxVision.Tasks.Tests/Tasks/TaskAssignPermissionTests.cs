using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// Darle trabajo a otro pide <c>tasks.assign</c>, también al crear. Tomar para uno mismo una tarea sin asignar de la
/// bandeja común basta con <c>tasks.write</c>.
/// </summary>
public sealed class TaskAssignPermissionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Beto = Guid.NewGuid();

    [Fact]
    public async Task Creating_a_task_for_someone_else_needs_the_assign_permission()
    {
        var forBeto = await CreateAsync(Beto, canAssignOthers: false);
        var forMe = await CreateAsync(Me, canAssignOthers: false);
        var allowed = await CreateAsync(Beto, canAssignOthers: true);

        Assert.Equal(TaskErrors.Forbidden, forBeto.Error);
        Assert.True(forMe.IsSuccess);
        Assert.True(allowed.IsSuccess);
    }

    [Fact]
    public async Task An_unassigned_task_from_the_common_inbox_can_be_taken()
    {
        var task = NewTask(assignee: null, createdBy: Beto);
        var tasks = new InMemoryTaskRepository(task);

        var taken = await TakeAsync(tasks, task);

        Assert.True(taken.IsSuccess);
        Assert.Equal(Me, task.AssigneeUserId);
    }

    [Fact]
    public async Task A_task_someone_already_has_cannot_be_taken()
    {
        var task = NewTask(assignee: Beto, createdBy: Beto);
        var tasks = new InMemoryTaskRepository(task);

        var taken = await TakeAsync(tasks, task);

        Assert.Equal(TaskErrors.AlreadyAssigned, taken.Error);
        Assert.Equal(Beto, task.AssigneeUserId);
    }

    private static Task<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>> TakeAsync(
        InMemoryTaskRepository tasks,
        TaskItem task
    ) =>
        TakeTaskHandler.Handle(
            new TakeTaskCommand(TenantId, task.Id, Me, CanViewAll: false),
            tasks,
            new RecordingUnitOfWork(),
            Options.Create(new TasksVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );

    private static Task<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>> CreateAsync(
        Guid assignee,
        bool canAssignOthers
    ) =>
        CreateTaskHandler.Handle(
            new CreateTaskCommand(
                TenantId,
                Me,
                "Prepare return",
                null,
                TaskPriority.Normal,
                assignee,
                null,
                null,
                null,
                null,
                false,
                null,
                CanAssignOthers: canAssignOthers
            ),
            new InMemoryTaskRepository(),
            new RecordingUnitOfWork(),
            new RecordingTaskMetrics(),
            new FixedOfficeZone("America/New_York"),
            new EveryCustomer(),
            CancellationToken.None
        );

    private static TaskItem NewTask(Guid? assignee, Guid createdBy) =>
        TaskItem
            .Create(
                TenantId,
                createdBy,
                TaskTitle.Create("Sort the mail").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                assignee,
                DateTime.UtcNow
            )
            .Value;
}
