using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>La tarea empezó o dejó de esperar a <paramref name="DependsOnTaskId"/>.</summary>
public sealed record TaskDependencyChangedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid DependsOnTaskId,
    bool Added,
    Guid ByUserId,
    DateTime AtUtc
) : IDomainEvent;
