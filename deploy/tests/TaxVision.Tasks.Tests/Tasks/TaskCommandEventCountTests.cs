using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Permissions.Abstractions;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Series.Commands;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Application.Templates;
using TaxVision.Tasks.Application.Templates.Commands;
using TaxVision.Tasks.Application.Templates.Seed;
using TaxVision.Tasks.Domain.Permissions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;
using TaxVision.Tasks.Tests.Templates;
using Wolverine;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// Un aviso por acción: cuenta lo emitido por cada comando, sumando lo publicado a mano y lo devuelto por los
/// handlers de eventos de dominio. Si quedan los dos caminos, el aviso sale dos veces y esto falla.
/// </summary>
public sealed class TaskCommandEventCountTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();
    private static readonly DateTime DueAtUtc = DateTime.UtcNow.AddDays(30);

    [Fact]
    public async Task Create_EmitsExactlyOneOfEachIntegrationEvent()
    {
        var fixture = new Fixture();

        await fixture.RunAsync(
            typeof(CreateTaskHandler),
            new CreateTaskCommand(TenantId, Manager, "Prepare return", null, TaskPriority.Normal, Preparer, null, null, DueAtUtc, "America/New_York", false, null)
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<TaskCreatedIntegrationEvent>());
        Assert.Single(emitted.OfType<ReminderRequestedIntegrationEvent>());
        Assert.Single(emitted.OfType<TaskAssignedIntegrationEvent>());
    }

    [Fact]
    public async Task ApplyTemplate_EmitsExactlyOneOfEachIntegrationEvent()
    {
        var fixture = new Fixture();
        var template = Template();

        await fixture.RunAsync(
            typeof(ApplyTaskTemplateHandler),
            new ApplyTaskTemplateCommand(TenantId, Manager, template.Id, Preparer, null, 2025, DueAtUtc, "America/New_York", false),
            new InMemoryTaskTemplateRepository(template)
        );

        var emitted = await fixture.EmittedAsync();
        var created = fixture.Tasks.Stored;
        Assert.NotEmpty(created);
        Assert.All(created, task => Assert.Single(emitted.OfType<TaskCreatedIntegrationEvent>(), e => e.TaskId == task.Id));
        Assert.All(created, task => Assert.Single(emitted.OfType<ReminderRequestedIntegrationEvent>(), e => e.TargetId == task.Id));
        Assert.Equal(created.Count, emitted.OfType<TaskCreatedIntegrationEvent>().Count());
        Assert.Empty(emitted.OfType<TaskAssignedIntegrationEvent>());
    }

    [Fact]
    public async Task CreateSeries_EmitsExactlyOneOfEachIntegrationEvent()
    {
        var fixture = new Fixture();

        await fixture.RunAsync(
            typeof(CreateTaskSeriesHandler),
            new CreateTaskSeriesCommand(TenantId, Manager, "Monthly bookkeeping", null, TaskPriority.Normal, null, null, null, Preparer, false, "FREQ=MONTHLY;INTERVAL=1", "America/New_York", RecurrenceMode.FixedSchedule, DateTime.UtcNow.AddDays(2), null, null)
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<TaskCreatedIntegrationEvent>());
        Assert.Single(emitted.OfType<ReminderRequestedIntegrationEvent>());
        Assert.Empty(emitted.OfType<TaskAssignedIntegrationEvent>());
    }

    [Fact]
    public async Task Reopen_EmitsExactlyOneOfEachIntegrationEvent()
    {
        var task = NewTask(Preparer);
        task.Complete(Manager, DateTime.UtcNow);
        var fixture = new Fixture(task);

        await fixture.RunAsync(typeof(ReopenTaskHandler), new ReopenTaskCommand(TenantId, task.Id, Manager, true));

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<TaskReopenedIntegrationEvent>());
        Assert.EndsWith(":r1", Assert.Single(emitted.OfType<ReminderRequestedIntegrationEvent>()).RequestKey);
    }

    [Fact]
    public async Task Assign_EmitsExactlyOneOfEachIntegrationEvent()
    {
        var task = NewTask(Manager);
        var fixture = new Fixture(task);

        await fixture.RunAsync(typeof(AssignTaskHandler), new AssignTaskCommand(TenantId, task.Id, Preparer, Manager, false));

        var emitted = await fixture.EmittedAsync();
        Assert.Equal(Preparer, Assert.Single(emitted.OfType<TaskAssignedIntegrationEvent>()).AssigneeUserId);
        Assert.Equal(Preparer, Assert.Single(emitted.OfType<ReminderRequestedIntegrationEvent>()).UserId);
    }

    [Fact]
    public async Task Complete_EmitsExactlyOneOfEachIntegrationEvent()
    {
        var task = NewTask(Preparer);
        var fixture = new Fixture(task);

        await fixture.RunAsync(typeof(CompleteTaskHandler), new CompleteTaskCommand(TenantId, task.Id, Manager, true));

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<TaskCompletedIntegrationEvent>());
        Assert.Single(emitted.OfType<ReminderTargetClosedIntegrationEvent>());
        Assert.Empty(emitted.OfType<ReminderRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task Cancel_EmitsExactlyOneOfEachIntegrationEvent()
    {
        var task = NewTask(Preparer);
        var fixture = new Fixture(task);

        await fixture.RunAsync(typeof(CancelTaskHandler), new CancelTaskCommand(TenantId, task.Id, Manager, true, "Client left"));

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<TaskCancelledIntegrationEvent>());
        Assert.Single(emitted.OfType<ReminderTargetClosedIntegrationEvent>());
    }

    private static TaskItem NewTask(Guid assignee) =>
        TaskItem
            .Create(
                TenantId,
                Manager,
                TaskTitle.Create("Prepare return").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                DueDate.Create(DueAtUtc, "America/New_York", false).Value,
                null,
                assignee,
                DateTime.UtcNow
            )
            .Value;

    private static TaskTemplate Template()
    {
        var standard = StandardTaxTemplates.All.First(t => t.RecurrenceRule is null);
        var template = TaskTemplate.Create(TenantId, Manager, standard.Name, standard.Description, DateTime.UtcNow).Value;
        Assert.True(TaskTemplateStepFactory.ApplyTo(template, standard.Steps).IsSuccess);
        return template;
    }

    /// <summary>Corre el handler del comando resolviendo sus parámetros por tipo, sin atarse a su firma.</summary>
    private sealed class Fixture(params TaskItem[] seed)
    {
        public InMemoryTaskRepository Tasks { get; } = new(seed);
        public FakeMessageBus Bus { get; } = new();

        public Task<IReadOnlyList<object>> EmittedAsync() => DomainEventReplay.EmittedAsync(Bus, Tasks);

        public async Task RunAsync(Type handler, object command, params object[] extra)
        {
            var series = new InMemoryTaskSeriesRepository();
            var services = new List<object>(extra)
            {
                Tasks,
                Bus,
                series,
                new TaskSeriesMaterializer(Tasks, series),
                new TaskTemplateInstantiator(Tasks, new InMemoryTaskDependencyRepository()),
                new RecordingUnitOfWork(),
                new NoOpCorrelationContext(),
                new RecordingTaskMetrics(),
                new NoOpCascades(),
                new NoUsers(),
            };
            var method = handler.GetMethod("Handle", BindingFlags.Public | BindingFlags.Static)!;
            var args = method
                .GetParameters()
                .Select(p =>
                    p.ParameterType == command.GetType() ? command
                    : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                    : services.First(s => p.ParameterType.IsInstanceOfType(s))
                )
                .ToArray();
            var result = await (dynamic)method.Invoke(null, args)!;
            Assert.True(((BuildingBlocks.Results.Result)result).IsSuccess);
        }
    }

    private sealed class NoOpCascades : ITaskUnblockingService, ITaskHierarchyService
    {
        public Task ApplyPredecessorClosedAsync(Guid tenantId, Guid predecessorTaskId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ApplyPredecessorReopenedAsync(Guid tenantId, Guid predecessorTaskId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ApplyChildClosedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ApplyChildReopenedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteWithDescendantsAsync(Guid tenantId, Guid taskId, Guid byUserId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoUsers : IUserPermissionsProjectionRepository
    {
        public Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<UserPermissionsProjection?>(null);

        public Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserPermissionsProjection>>([]);
    }
}
