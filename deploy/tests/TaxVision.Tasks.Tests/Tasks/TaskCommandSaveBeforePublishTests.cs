using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Tasks.Application.Attachments.Abstractions;
using TaxVision.Tasks.Application.Attachments.Commands;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Application.ClientRequests.Commands;
using TaxVision.Tasks.Application.Dependencies;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.ClientRequests;
using TaxVision.Tasks.Tests.Dependencies;
using Wolverine;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// BB-14: por InvokeAsync, lo publicado antes de un guardado fallido sale igual tras el reintento en línea. El guardado
/// falla como un timeout de SQL y no debe quedar nada en el bus. Con el guardado bien, sale lo mismo que antes.
/// </summary>
public sealed class TaskCommandSaveBeforePublishTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly DateTime DueAtUtc = DateTime.UtcNow.AddDays(30);

    public static TheoryData<string> Commands =>
        [
            "MoveToWaitingOnClient",
            "ChangeDue",
            "Complete",
            "Cancel",
            "Delete",
            "CreateClientRequest",
            "RecordClientDocument",
            "UploadAttachment",
            "LinkAttachment",
            "DetachAttachment",
        ];

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task WhenSaveFails_PublishesNothing(string command)
    {
        var fixture = new Fixture(new FailingUnitOfWork());

        await Assert.ThrowsAsync<TimeoutException>(() => fixture.RunAsync(command));

        Assert.Empty(fixture.Bus.Published);
    }

    [Fact]
    public async Task MoveToWaitingOnClient_WhenSaved_PublishesInTheSameOrder()
    {
        var fixture = new Fixture(new RecordingUnitOfWork());

        await fixture.RunAsync("MoveToWaitingOnClient");

        Assert.Collection(
            fixture.Bus.Published,
            message => Assert.IsType<TaskWaitingOnClientIntegrationEvent>(message),
            message => Assert.IsType<ReminderRequestedIntegrationEvent>(message)
        );
    }

    [Fact]
    public async Task ChangeDue_WhenSaved_PublishesInTheSameOrder()
    {
        var fixture = new Fixture(new RecordingUnitOfWork());

        await fixture.RunAsync("ChangeDue");

        Assert.Collection(
            fixture.Bus.Published,
            message => Assert.IsType<TaskDueChangedIntegrationEvent>(message),
            message => Assert.IsType<ReminderTargetMovedIntegrationEvent>(message)
        );
    }

    [Theory]
    [InlineData("Complete")]
    [InlineData("Cancel")]
    public async Task Closing_WhenSaved_AnnouncesTheUnblockedSuccessor(string command)
    {
        var fixture = new Fixture(new RecordingUnitOfWork());

        await fixture.RunAsync(command);

        var unblocked = Assert.IsType<TaskUnblockedIntegrationEvent>(Assert.Single(fixture.Bus.Published));
        Assert.Equal(fixture.Successor.Id, unblocked.TaskId);
    }

    [Fact]
    public async Task Delete_WhenSaved_PublishesInTheSameOrder()
    {
        var fixture = new Fixture(new RecordingUnitOfWork());

        await fixture.RunAsync("Delete");

        Assert.Collection(
            fixture.Bus.Published,
            message => Assert.Equal(fixture.Successor.Id, Assert.IsType<TaskUnblockedIntegrationEvent>(message).TaskId),
            message =>
                Assert.Equal(fixture.Item.Id, Assert.IsType<ReminderTargetClosedIntegrationEvent>(message).TargetId)
        );
    }

    private sealed class Fixture
    {
        private readonly Guid _attachedFileId = Guid.NewGuid();
        private readonly ClientRequest _request;
        private readonly IServiceProvider _services;

        public Fixture(IUnitOfWork unitOfWork)
        {
            Item = NewTask();
            Successor = NewTask();
            Successor.RegisterBlockerAdded();
            Assert.True(
                Item.LinkExistingFile(
                    _attachedFileId,
                    "W-2.pdf",
                    "application/pdf",
                    10,
                    Owner,
                    DateTime.UtcNow
                ).IsSuccess
            );
            _request = ClientRequest
                .Create(TenantId, CustomerId, Owner, null, "W-2 2025", null, null, DateTime.UtcNow)
                .Value;

            var tasks = new InMemoryTaskRepository(Item, Successor);
            var dependencies = new InMemoryTaskDependencyRepository();
            dependencies.Seed(TenantId, Successor.Id, Item.Id);
            var series = new InMemoryTaskSeriesRepository();

            var services = new ServiceCollection()
                .AddSingleton<IMessageBus>(Bus)
                .AddSingleton<ITaskRepository>(tasks)
                .AddSingleton<ITaskDependencyRepository>(dependencies)
                .AddSingleton<ICorrelationContext>(new NoOpCorrelationContext())
                .AddSingleton(unitOfWork)
                .AddSingleton<ITaskMetrics>(new RecordingTaskMetrics())
                .AddSingleton<ITaskHierarchyService>(new NoOpHierarchy())
                .AddSingleton<IClientRequestRepository>(new InMemoryClientRequestRepository(_request))
                .AddSingleton<ITaskSeriesMaterializer>(new TaskSeriesMaterializer(tasks, series))
                .AddSingleton<ITaskUnblockingService>(sp =>
                    ActivatorUtilities.CreateInstance<TaskUnblockingService>(sp)
                );
            _services = services.BuildServiceProvider();
        }

        public TaskItem Item { get; }
        public TaskItem Successor { get; }
        public FakeMessageBus Bus { get; } = new();

        public Task RunAsync(string command) =>
            command switch
            {
                "MoveToWaitingOnClient" => InvokeAsync(
                    typeof(MoveTaskToWaitingOnClientHandler),
                    new MoveTaskToWaitingOnClientCommand(TenantId, Item.Id, Owner, false, "W-2 2025", DueAtUtc)
                ),
                "ChangeDue" => InvokeAsync(
                    typeof(ChangeTaskDueHandler),
                    new ChangeTaskDueCommand(TenantId, Item.Id, Owner, false, DueAtUtc, "America/New_York", false, null)
                ),
                "Complete" => InvokeAsync(
                    typeof(CompleteTaskHandler),
                    new CompleteTaskCommand(TenantId, Item.Id, Owner, false)
                ),
                "Cancel" => InvokeAsync(
                    typeof(CancelTaskHandler),
                    new CancelTaskCommand(TenantId, Item.Id, Owner, false, "not needed")
                ),
                "Delete" => InvokeAsync(
                    typeof(DeleteTaskHandler),
                    new DeleteTaskCommand(TenantId, Item.Id, Owner, false)
                ),
                "CreateClientRequest" => InvokeAsync(
                    typeof(CreateClientRequestHandler),
                    new CreateClientRequestCommand(TenantId, Owner, CustomerId, Item.Id, "W-2 2025", null, DueAtUtc)
                ),
                "RecordClientDocument" => InvokeAsync(
                    typeof(RecordClientDocumentHandler),
                    new RecordClientDocumentCommand(
                        new SubmitClientDocumentCommand(
                            TenantId,
                            CustomerId,
                            _request.Id,
                            Guid.NewGuid(),
                            "W-2.pdf",
                            "application/pdf",
                            10
                        ),
                        RemoteFileScanStatus.Available
                    )
                ),
                "UploadAttachment" => InvokeAsync(
                    typeof(UploadTaskAttachmentHandler),
                    new UploadTaskAttachmentCommand(
                        TenantId,
                        Owner,
                        Item.Id,
                        Guid.NewGuid(),
                        "W-2.pdf",
                        "application/pdf",
                        10
                    )
                ),
                "LinkAttachment" => InvokeAsync(
                    typeof(LinkTaskAttachmentHandler),
                    new LinkTaskAttachmentCommand(
                        TenantId,
                        Owner,
                        Item.Id,
                        Guid.NewGuid(),
                        "W-2.pdf",
                        "application/pdf",
                        10
                    )
                ),
                "DetachAttachment" => InvokeAsync(
                    typeof(DetachTaskAttachmentHandler),
                    new DetachTaskAttachmentCommand(TenantId, Item.Id, _attachedFileId, Owner)
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            };

        // Resuelve los parámetros por tipo, sin atarse a la firma del handler.
        private async Task InvokeAsync(Type handler, object command)
        {
            var method = handler.GetMethod("Handle", BindingFlags.Public | BindingFlags.Static)!;
            var args = method
                .GetParameters()
                .Select(p =>
                    p.ParameterType == command.GetType() ? command
                    : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                    : _services.GetRequiredService(p.ParameterType)
                )
                .ToArray();
            var result = await (dynamic)method.Invoke(null, args)!;
            Assert.True(((BuildingBlocks.Results.Result)result).IsSuccess);
        }

        private static TaskItem NewTask() =>
            TaskItem
                .Create(
                    TenantId,
                    Owner,
                    TaskTitle.Create("Prepare return").Value,
                    null,
                    TaskPriority.Normal,
                    TaskReference.Create(CustomerId, 2025).Value,
                    DueDate.Create(DueAtUtc.AddDays(5), "America/New_York", false).Value,
                    null,
                    Owner,
                    DateTime.UtcNow
                )
                .Value;
    }

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            throw new TimeoutException("Simulated SQL timeout.");
    }

    private sealed class NoOpHierarchy : ITaskHierarchyService
    {
        public Task ApplyChildClosedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ApplyChildReopenedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteWithDescendantsAsync(
            Guid tenantId,
            Guid taskId,
            Guid byUserId,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }
}
