using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

/// <param name="ExpectedItems">Qué se le pide al cliente. Viaja hasta el correo, así que es obligatorio.</param>
/// <param name="ClientDueAtUtc">Para cuándo se lo pide, distinta del vencimiento de la tarea.</param>
/// <param name="ClientDueOn">Fecha civil: si viene, manda y vence al final de ese día en la oficina.</param>
public sealed record MoveTaskToWaitingOnClientCommand(
    Guid TenantId,
    Guid TaskId,
    Guid ByUserId,
    bool HasManageAll,
    string? ExpectedItems,
    DateTime? ClientDueAtUtc,
    DateOnly? ClientDueOn = null
) : ITaskTransitionCommand;

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
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        var expectedItems = ClientRequestNote.Create(command.ExpectedItems);
        if (expectedItems.IsFailure)
            return Result.Failure<TaskResponse>(expectedItems.Error);

        var found = await TaskTransitionService.LoadMutableAsync(tasks, command, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);

        var task = found.Value;
        if (task.Reference.CustomerId is not { } customerId)
            return Result.Failure<TaskResponse>(TaskErrors.WaitingOnClient.CustomerRequired);

        var clientDueAtUtc = await ClientDueAtUtcAsync(command, zones, ct);
        var moved = task.MoveToWaitingOnClient(
            expectedItems.Value,
            clientDueAtUtc,
            command.ByUserId,
            DateTime.UtcNow,
            command.ClientDueOn
        );
        if (moved.IsFailure)
            return Result.Failure<TaskResponse>(moved.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(BuildEvent(task, customerId, correlation.CorrelationId));
        var zone = await zones.ResolveAsync(command.TenantId, ct);
        await RequestPreparerReminderAsync(task, clientDueAtUtc, zone.Id, bus, correlation);

        return Result.Success(TaskResponse.From(task));
    }

    /// <summary>
    /// El recordatorio es para el <b>preparador</b>, no para el cliente: «hace días que le pediste el
    /// W-2 a Pérez». El contrato de Reminder exige un <c>UserId</c> y el cliente no tiene uno — a él
    /// se le escribe por Notification, que es otro camino. Sin fecha pedida no hay ancla, y sin
    /// asignado no hay a quién recordarle.
    /// </summary>
    // Fecha civil: vence al final de ese día en la oficina.
    internal static async Task<DateTime?> ClientDueAtUtcAsync(
        MoveTaskToWaitingOnClientCommand command,
        IOfficeTimeZone zones,
        CancellationToken ct
    ) =>
        command.ClientDueOn is { } civil
            ? LocalDay.EndUtc(civil, await zones.ResolveAsync(command.TenantId, ct))
            : command.ClientDueAtUtc;

    internal static async Task RequestPreparerReminderAsync(
        TaskItem task,
        DateTime? clientDueAtUtc,
        string officeTimeZoneId,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        if (clientDueAtUtc is not { } dueAtUtc || task.AssigneeUserId is not { } preparer)
            return;

        await bus.PublishAsync(
            TaskReminderContracts.ClientResponseExpected(
                task,
                preparer,
                dueAtUtc,
                officeTimeZoneId,
                correlation.CorrelationId
            )
        );
    }

    internal static TaskWaitingOnClientIntegrationEvent BuildEvent(
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
            ClientDueOn = task.ClientDueCivilDate,
            RequestedByUserId = task.ClientRequestedByUserId!.Value,
            RequestedAtUtc = task.ClientRequestedAtUtc!.Value,
        };
}
