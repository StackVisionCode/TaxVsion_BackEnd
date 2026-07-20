using BuildingBlocks.Domain;

namespace TaxVision.Codes.Domain.Events;

public sealed record CodeReservationExpiredDomainEvent(
    Guid ReservationId,
    Guid TenantId,
    Guid CodeDefinitionId,
    DateTime OccurredAtUtc
) : IDomainEvent;
