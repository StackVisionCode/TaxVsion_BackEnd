using BuildingBlocks.Domain;

namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>Cambió lo que se ve del hilo. El aviso relee el estado y los no leídos después del commit.</summary>
public sealed record EmailThreadStateChangedDomainEvent(
    Guid TenantId,
    Guid EmailThreadId,
    Guid? ActorUserId,
    string CorrelationId
) : IDomainEvent;
