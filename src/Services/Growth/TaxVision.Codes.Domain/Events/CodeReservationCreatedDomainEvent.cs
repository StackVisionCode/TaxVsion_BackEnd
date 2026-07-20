using BuildingBlocks.Domain;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Events;

public sealed record CodeReservationCreatedDomainEvent(
    Guid ReservationId,
    Guid TenantId,
    Guid QuoteId,
    Guid CodeDefinitionId,
    PaymentReference Payment,
    DateTime OccurredAtUtc
) : IDomainEvent;
