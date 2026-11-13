using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.PublicBookings.Events;

/// <summary>La reserva se canceló.</summary>
public sealed record PublicBookingCancelledDomainEvent(
    Guid BookingId,
    Guid TenantId,
    DateTime SlotStartUtc,
    Guid HostUserId,
    PublicBookingActor CancelledBy,
    string? Reason,
    int IcsSequence,
    DateTime OccurredAtUtc
) : IDomainEvent;
