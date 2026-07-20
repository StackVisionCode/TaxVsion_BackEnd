using BuildingBlocks.Domain;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Events;

public sealed record CodeReservationCommittedDomainEvent(
    Guid ReservationId,
    Guid TenantId,
    Guid RedemptionId,
    PaymentReference Payment,
    Money GrossAmount,
    Money DiscountAmount,
    Money NetAmount,
    bool WasLateCommit,
    DateTime OccurredAtUtc
) : IDomainEvent;
