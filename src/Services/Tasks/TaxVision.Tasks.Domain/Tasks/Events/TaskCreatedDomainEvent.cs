using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>
/// La tarea existe. Dispara el aviso al asignado cuando nace asignada a otro. <paramref name="RecurringOccurrence"/>:
/// la sembró una serie que ya existía (no su alta); por D-TSK-1 no avisa la asignación.
/// </summary>
public sealed record TaskCreatedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid CreatedByUserId,
    Guid? AssigneeUserId,
    Guid? ParentTaskId,
    DateTime CreatedAtUtc,
    bool RecurringOccurrence = false
) : IDomainEvent;
