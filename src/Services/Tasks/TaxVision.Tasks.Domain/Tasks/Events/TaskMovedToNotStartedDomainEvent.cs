using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>La tarea volvió a Not Started desde el tablero (desde In Progress o Waiting on Client).</summary>
public sealed record TaskMovedToNotStartedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    TaskItemStatus PreviousStatus,
    Guid MovedByUserId,
    DateTime MovedAtUtc
) : IDomainEvent;
