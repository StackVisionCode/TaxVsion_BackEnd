using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>Lo que trae un movimiento según la etapa destino: lo pedido al cliente o el motivo de cancelar.</summary>
public sealed record TaskMoveRequest(
    ClientRequestNote? ExpectedItems,
    DateTime? ClientDueAtUtc,
    DateOnly? ClientDueOn,
    string? CancelReason
)
{
    public static readonly TaskMoveRequest None = new(null, null, null, null);
}

/// <summary>Qué cascadas pide el movimiento: el handler desbloquea, ajusta a la madre y avanza la serie.</summary>
public sealed record TaskMoveOutcome(bool Reopened, bool Closed, bool Started, bool MovedToWaitingOnClient);

public sealed partial class TaskItem
{
    /// <summary>
    /// Mueve la tarea a un estado de la oficina. Dentro de la misma etapa solo cambia el estado; a otra
    /// etapa aplica su regla con los métodos de siempre. Valida antes de tocar nada: si la regla no se
    /// cumple, la tarea no cambia.
    /// </summary>
    public Result<TaskMoveOutcome> MoveTo(
        WorkflowStatusRef target,
        TaskMoveRequest request,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var fromCategory = Status;
        var fromStatusId = WorkflowStatusId;
        if (target.Category == Status)
        {
            if (target.Id != WorkflowStatusId)
                SetWorkflowStatus(target, fromCategory, fromStatusId, byUserId, nowUtc);
            return Result.Success(new TaskMoveOutcome(false, false, false, false));
        }

        var allowed = CanEnter(target.Category, request);
        if (allowed.IsFailure)
            return Result.Failure<TaskMoveOutcome>(allowed.Error);

        var reopened = IsClosed && Reopen(byUserId, nowUtc).IsSuccess;
        var entered = Enter(target.Category, request, byUserId, nowUtc);
        if (entered.IsFailure)
            return Result.Failure<TaskMoveOutcome>(entered.Error);

        SetWorkflowStatus(target, fromCategory, fromStatusId, byUserId, nowUtc);
        return Result.Success(
            new TaskMoveOutcome(
                reopened,
                IsClosed,
                target.Category == TaskItemStatus.InProgress,
                target.Category == TaskItemStatus.WaitingOnClient
            )
        );
    }

    /// <summary>Las reglas de la etapa destino, sin cambiar nada.</summary>
    private Result CanEnter(TaskItemStatus category, TaskMoveRequest request) =>
        category switch
        {
            TaskItemStatus.InProgress when IsBlocked => Result.Failure(
                TaskErrors.BlockedByDependencies(OpenBlockerCount)
            ),
            TaskItemStatus.Completed when IsBlocked => Result.Failure(
                TaskErrors.BlockedByDependencies(OpenBlockerCount)
            ),
            TaskItemStatus.Completed when OpenSubtaskCount > 0 => Result.Failure(
                TaskErrors.HasOpenSubtasks(OpenSubtaskCount)
            ),
            TaskItemStatus.Cancelled when string.IsNullOrWhiteSpace(request.CancelReason) => Result.Failure(
                TaskErrors.CancellationReasonRequired
            ),
            TaskItemStatus.Cancelled
                when request.CancelReason!.Trim().Length > TaskErrors.CancellationReasonMaxLength => Result.Failure(
                TaskErrors.CancellationReasonTooLong
            ),
            TaskItemStatus.WaitingOnClient when request.ExpectedItems is null => Result.Failure(
                TaskErrors.WaitingOnClient.ExpectedItemsRequired
            ),
            TaskItemStatus.WaitingOnClient when request.ClientDueAtUtc is { Kind: not DateTimeKind.Utc } =>
                Result.Failure(TaskErrors.WaitingOnClient.ClientDueNotUtc),
            _ => Result.Success(),
        };

    private Result Enter(TaskItemStatus category, TaskMoveRequest request, Guid byUserId, DateTime nowUtc) =>
        category switch
        {
            TaskItemStatus.NotStarted => MoveToNotStarted(byUserId, nowUtc),
            TaskItemStatus.InProgress => Start(byUserId, nowUtc),
            TaskItemStatus.WaitingOnClient => MoveToWaitingOnClient(
                request.ExpectedItems!,
                request.ClientDueAtUtc,
                byUserId,
                nowUtc,
                request.ClientDueOn
            ),
            TaskItemStatus.Completed => Complete(byUserId, nowUtc),
            TaskItemStatus.Cancelled => Cancel(request.CancelReason, byUserId, nowUtc),
            _ => Result.Failure(TaskErrors.InvalidTransition(Status, nameof(MoveTo))),
        };

    /// <summary>Al cambiar de etapa por un verbo, la tarea queda en el estado por defecto de la nueva.</summary>
    private void ChangeCategory(TaskItemStatus next)
    {
        if (next != Status)
        {
            WorkflowStatusId = null;
            WorkflowStatusCode = null;
        }
        if (Status == TaskItemStatus.WaitingOnClient && next != TaskItemStatus.WaitingOnClient)
            ClearClientRequest();
        Status = next;
    }

    /// <summary>Lo pedido al cliente vale mientras se espera; el historial conserva la espera.</summary>
    private void ClearClientRequest()
    {
        ExpectedItems = null;
        ClientDueAtUtc = null;
        ClientDueOn = null;
        ClientRequestedByUserId = null;
        ClientRequestedAtUtc = null;
        AddDomainEvent(new TaskLeftWaitingOnClientDomainEvent(Id, TenantId));
    }

    private void SetWorkflowStatus(
        WorkflowStatusRef target,
        TaskItemStatus fromCategory,
        Guid? fromStatusId,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        WorkflowStatusId = target.Id;
        WorkflowStatusCode = target.Id is null ? null : target.Code;
        AddDomainEvent(
            new TaskWorkflowStatusChangedDomainEvent(
                Id,
                TenantId,
                fromStatusId,
                target.Id,
                fromCategory,
                target.Category,
                byUserId,
                nowUtc
            )
        );
    }
}
