using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Attachments.Queries;
using TaxVision.Tasks.Application.Dependencies.Queries;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Timers.Abstractions;
using TaxVision.Tasks.Application.Timers.Queries;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// Temporizadores, adjuntos y grafo se leen con la misma visibilidad que la tarea; las horas de otra persona o de toda
/// la oficina piden <c>users.work.view</c>.
/// </summary>
public sealed class TaskReadGateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private static readonly IOptions<TasksVisibilityOptions> On = Options.Create(
        new TasksVisibilityOptions { Enabled = true }
    );

    [Fact]
    public async Task Someone_who_cannot_see_the_task_does_not_see_its_timers_files_or_graph()
    {
        var task = ClientTask();
        var tasks = new InMemoryTaskRepository(task);

        var timers = await ListTaskTimersHandler.Handle(new(TenantId, task.Id, Stranger, false), tasks, On, default);
        var files = await ListTaskAttachmentsHandler.Handle(
            new(TenantId, task.Id, false, Stranger, false),
            tasks,
            On,
            default
        );
        var graph = await GetTaskDependencyGraphHandler.Handle(
            new(TenantId, task.Id, Stranger, false),
            new InMemoryTaskDependencyRepository(),
            tasks,
            On,
            default
        );

        Assert.Equal(TaskErrors.NotFound, timers.Error);
        Assert.Equal(TaskErrors.NotFound, files.Error);
        Assert.Equal(TaskErrors.NotFound, graph.Error);
    }

    [Fact]
    public async Task The_assignee_and_whoever_sees_every_client_do_see_them()
    {
        var task = ClientTask();
        var tasks = new InMemoryTaskRepository(task);

        var mine = await ListTaskTimersHandler.Handle(new(TenantId, task.Id, Owner, false), tasks, On, default);
        var all = await ListTaskTimersHandler.Handle(new(TenantId, task.Id, Stranger, true), tasks, On, default);

        Assert.True(mine.IsSuccess);
        Assert.True(all.IsSuccess);
    }

    [Fact]
    public async Task Hours_of_someone_else_need_the_work_view_permission()
    {
        var report = new RecordingReport();

        var other = await GetTaskTimerReportHandler.Handle(
            new(TenantId, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, Stranger, Owner, false),
            report,
            default
        );
        var office = await GetTaskTimerReportHandler.Handle(
            new(TenantId, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, null, Owner, false),
            report,
            default
        );
        var allowed = await GetTaskTimerReportHandler.Handle(
            new(TenantId, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, Stranger, Owner, true),
            report,
            default
        );

        Assert.Equal(TaskErrors.Forbidden, other.Error);
        Assert.True(office.IsSuccess);
        Assert.True(allowed.IsSuccess);
        // Sin permiso, "toda la oficina" se reduce a las horas propias.
        Assert.Equal(new Guid?[] { Owner, Stranger }, report.AskedFor);
    }

    private static TaskItem ClientTask() =>
        TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.Create(Guid.NewGuid(), 2025).Value,
                null,
                null,
                Owner,
                DateTime.UtcNow
            )
            .Value;

    private sealed class RecordingReport : ITaskTimerRepository
    {
        public List<Guid?> AskedFor { get; } = [];

        public Task<IReadOnlyList<TaskTimerReportRow>> ListReportAsync(
            Guid tenantId,
            DateTime fromUtc,
            DateTime toUtc,
            Guid? userId,
            CancellationToken ct = default
        )
        {
            AskedFor.Add(userId);
            return Task.FromResult<IReadOnlyList<TaskTimerReportRow>>([]);
        }
    }
}
