using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

/// <param name="ExpectedItems">Qué se le pide al cliente. Viaja hasta el correo, así que es obligatorio.</param>
/// <param name="ClientDueAtUtc">Para cuándo se lo pide, distinta del vencimiento de la tarea.</param>
public sealed record MoveTaskToWaitingOnClientCommand(
    Guid TenantId,
    Guid TaskId,
    Guid ByUserId,
    bool HasManageAll,
    string? ExpectedItems,
    DateTime? ClientDueAtUtc
);

/// <summary>
/// Exige un cliente en la tarea: sin <c>CustomerId</c> no hay a quién pedirle nada y el evento
/// llegaría a Notification sin destinatario posible.
/// </summary>
public static class MoveTaskToWaitingOnClientHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        MoveTaskToWaitingOnClientCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var expectedItems = ClientRequestNote.Create(command.ExpectedItems);
        if (expectedItems.IsFailure)
            return Result.Failure<TaskResponse>(expectedItems.Error);

        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);

        var task = found.Value;
        if (!TaskAccessPolicy.CanMutate(task, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskResponse>(TaskErrors.Forbidden);

        if (task.Reference.CustomerId is not { } customerId)
            return Result.Failure<TaskResponse>(TaskErrors.WaitingOnClient.CustomerRequired);

        var nowUtc = DateTime.UtcNow;
        var moved = task.MoveToWaitingOnClient(expectedItems.Value, command.ClientDueAtUtc, command.ByUserId, nowUtc);
        if (moved.IsFailure)
            return Result.Failure<TaskResponse>(moved.Error);

        await bus.PublishAsync(BuildEvent(task, customerId, correlation.CorrelationId));
        await RequestPreparerReminderAsync(task, command.ClientDueAtUtc, bus, correlation);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(TaskResponse.From(task));
    }

    /// <summary>
    /// El recordatorio es para el <b>preparador</b>, no para el cliente: «hace días que le pediste el
    /// W-2 a Pérez». El contrato de Reminder exige un <c>UserId</c> y el cliente no tiene uno — a él
    /// se le escribe por Notification, que es otro camino. Sin fecha pedida no hay ancla, y sin
    /// asignado no hay a quién recordarle.
    /// </summary>
    private static async Task RequestPreparerReminderAsync(
        TaskItem task,
        DateTime? clientDueAtUtc,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        if (clientDueAtUtc is not { } dueAtUtc || task.AssigneeUserId is not { } preparer)
            return;

        await bus.PublishAsync(
            TaskReminderContracts.ClientResponseExpected(task, preparer, dueAtUtc, correlation.CorrelationId)
        );
    }

    private static TaskWaitingOnClientIntegrationEvent BuildEvent(
        TaskItem task,
        Guid customerId,
        string correlationId
    ) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            TaskId = task.Id,
            Title = task.Title.Value,
            CustomerId = customerId,
            TaxYear = task.Reference.TaxYear,
            ExpectedItems = task.ExpectedItems!.Value,
            ClientDueAtUtc = task.ClientDueAtUtc,
            RequestedByUserId = task.ClientRequestedByUserId!.Value,
            RequestedAtUtc = task.ClientRequestedAtUtc!.Value,
        };
}
