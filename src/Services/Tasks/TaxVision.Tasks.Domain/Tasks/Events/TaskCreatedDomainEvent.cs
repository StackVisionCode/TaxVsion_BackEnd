using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>La tarea existe. Dispara el aviso al asignado cuando nace asignada a otro.</summary>
public sealed record TaskCreatedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid CreatedByUserId,
    Guid? AssigneeUserId,
    Guid? ParentTaskId,
    DateTime CreatedAtUtc
) : IDomainEvent;
