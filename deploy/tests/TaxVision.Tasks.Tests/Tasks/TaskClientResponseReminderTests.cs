using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Application.Tasks.DomainEvents;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// El aviso "el cliente no respondió" es de cada espera: tiene su categoría (mover o cerrar el vencimiento no lo toca),
/// su clave por ciclo y se cierra al salir de Waiting on client, que además deja de mostrar lo pedido.
/// </summary>
public sealed class TaskClientResponseReminderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();

    [Fact]
    public async Task Each_wait_on_the_client_has_its_own_reminder_in_english_and_in_the_office_zone()
    {
        var task = NewTask();
        var tasks = new InMemoryTaskRepository(task);
        var bus = new FakeMessageBus();

        await WaitAsync(task, tasks, bus, new DateOnly(2026, 12, 1));
        task.Start(Owner, DateTime.UtcNow);
        await Task.Delay(5);
        await WaitAsync(task, tasks, bus, new DateOnly(2026, 12, 15));

        var reminders = bus.Published.OfType<ReminderRequestedIntegrationEvent>().ToList();
        Assert.Equal(2, reminders.Count);
        Assert.All(
            reminders,
            r =>
            {
                Assert.Equal(TaskReminderContracts.ClientResponseCategory, r.Category);
                Assert.Equal("America/New_York", r.TimeZoneId);
                Assert.StartsWith("No reply from the client:", r.Title);
            }
        );
        Assert.NotEqual(reminders[0].RequestKey, reminders[1].RequestKey);
    }

    [Fact]
    public void Leaving_waiting_on_the_client_clears_what_was_asked_and_closes_only_its_reminder()
    {
        var task = NewTask();
        task.MoveToWaitingOnClient(
            ClientRequestNote.Create("W-2").Value,
            DateTime.UtcNow.AddDays(5),
            Owner,
            DateTime.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))
        );
        task.ClearDomainEvents();

        task.Start(Owner, DateTime.UtcNow);

        Assert.Null(task.ExpectedItems);
        Assert.Null(task.ClientDueAtUtc);
        Assert.Null(task.ClientRequestedByUserId);
        var left = Assert.Single(task.DomainEvents.OfType<TaskLeftWaitingOnClientDomainEvent>());
        var closed = TaskLeftWaitingOnClientDomainEventHandler.Handle(left, new NoOpCorrelationContext());
        Assert.Equal((TaskReminderContracts.ClientResponseCategory, task.Id), (closed.Category, closed.TargetId));
    }

    [Fact]
    public void Deleting_a_task_closes_its_reminders_with_a_known_reason()
    {
        Assert.Equal("deleted", TaskReminderContracts.ClosureReasons.Deleted);
    }

    private static Task WaitAsync(
        TaskItem task,
        InMemoryTaskRepository tasks,
        FakeMessageBus bus,
        DateOnly clientDueOn
    ) =>
        MoveTaskToWaitingOnClientHandler.Handle(
            new MoveTaskToWaitingOnClientCommand(TenantId, task.Id, Owner, true, "W-2", null, ClientDueOn: clientDueOn),
            tasks,
            new RecordingUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            new FixedOfficeZone("America/New_York"),
            CancellationToken.None
        );

    private static TaskItem NewTask() =>
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
                Preparer,
                DateTime.UtcNow
            )
            .Value;
}
