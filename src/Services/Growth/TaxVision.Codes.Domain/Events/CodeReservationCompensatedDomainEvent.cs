using BuildingBlocks.Domain;

namespace TaxVision.Codes.Domain.Events;

public sealed record CodeReservationCompensatedDomainEvent(
    Guid ReservationId,
    Guid TenantId,
    Guid RedemptionId,
    Guid CompensationId,
    bool IsFinal,
    DateTime OccurredAtUtc
) : IDomainEvent;
