using BuildingBlocks.Domain;

namespace TaxVision.Codes.Domain.Events;

public sealed record CodeReservationCancelledDomainEvent(
    Guid ReservationId,
    Guid TenantId,
    Guid CodeDefinitionId,
    string Reason,
    DateTime OccurredAtUtc
) : IDomainEvent;
