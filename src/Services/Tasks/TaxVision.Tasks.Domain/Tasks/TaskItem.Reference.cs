using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>Cliente y año fiscal: la raíz los cambia y sus subtareas los heredan.</summary>
public sealed partial class TaskItem
{
    /// <summary>Cliente y año fiscal de una tarea raíz. Sus subtareas los heredan con <see cref="InheritReference"/>.</summary>
    public Result ChangeReference(TaskReference reference, Guid byUserId, DateTime nowUtc)
    {
        if (IsClosed)
            return Result.Failure(TaskErrors.InvalidTransition(Status, nameof(ChangeReference)));
        if (ParentTaskId is not null)
            return Result.Failure(TaskErrors.Reference.FollowsParent);
        if (reference == Reference)
            return Result.Success();
        // El pedido en curso es de ese cliente: cambiarlo dejaría el aviso y la respuesta a nombre de otro.
        if (Status == TaskItemStatus.WaitingOnClient && reference.CustomerId != Reference.CustomerId)
            return Result.Failure(TaskErrors.Reference.CustomerLockedWhileWaiting);

        var previous = Reference;
        Reference = reference.CopyForNewRow();
        AddDomainEvent(
            new TaskReferenceChangedDomainEvent(
                Id,
                TenantId,
                previous.CustomerId,
                reference.CustomerId,
                previous.TaxYear,
                reference.TaxYear,
                byUserId,
                nowUtc
            )
        );
        return Result.Success();
    }

    /// <summary>La subtarea toma el cliente y el año de su raíz, sin entrada propia en el historial.</summary>
    public void InheritReference(TaskReference reference)
    {
        if (ParentTaskId is not null && reference != Reference)
            Reference = reference.CopyForNewRow();
    }
}
