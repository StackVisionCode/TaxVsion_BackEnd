using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>Se empezó a trabajar. Es el instante que necesita la métrica de tiempo de ciclo.</summary>
public sealed record TaskStartedDomainEvent(Guid TaskId, Guid TenantId, Guid StartedByUserId, DateTime StartedAtUtc)
    : IDomainEvent;
