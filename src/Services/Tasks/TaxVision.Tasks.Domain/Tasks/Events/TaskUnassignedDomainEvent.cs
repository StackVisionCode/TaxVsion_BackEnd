using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>La tarea quedó sin responsable: es lo que la bandeja del equipo tiene que mostrar.</summary>
public sealed record TaskUnassignedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid PreviousAssigneeUserId,
    Guid UnassignedByUserId,
    DateTime UnassignedAtUtc
) : IDomainEvent;
