using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>La tarea pasó a otro estado de la oficina. Un id nulo es el estado por defecto de su etapa.</summary>
public sealed record TaskWorkflowStatusChangedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid? FromStatusId,
    Guid? ToStatusId,
    TaskItemStatus FromCategory,
    TaskItemStatus ToCategory,
    Guid ByUserId,
    DateTime AtUtc
) : IDomainEvent;
