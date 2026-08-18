using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>Cambió la prioridad. No altera ninguna regla; es para el hilo de actividad y el aviso.</summary>
public sealed record TaskPriorityChangedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    TaskPriority PreviousPriority,
    TaskPriority NewPriority,
    Guid ChangedByUserId,
    DateTime ChangedAtUtc
) : IDomainEvent;
