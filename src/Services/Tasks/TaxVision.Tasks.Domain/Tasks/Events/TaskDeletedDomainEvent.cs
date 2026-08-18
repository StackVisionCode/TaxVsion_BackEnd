using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <param name="WasClosed">Si estaba abierta, el padre tiene que descontarla.</param>
public sealed record TaskDeletedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid? ParentTaskId,
    bool WasClosed,
    Guid DeletedByUserId,
    DateTime DeletedAtUtc
) : IDomainEvent;
