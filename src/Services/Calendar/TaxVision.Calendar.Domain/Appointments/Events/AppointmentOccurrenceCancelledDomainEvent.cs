using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>Se cancelo UNA ocurrencia de la serie; la serie sigue.</summary>
public sealed record AppointmentOccurrenceCancelledDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    DateTime OriginalStartUtc,
    Guid CancelledByUserId,
    DateTime CancelledAtUtc
) : IDomainEvent;
