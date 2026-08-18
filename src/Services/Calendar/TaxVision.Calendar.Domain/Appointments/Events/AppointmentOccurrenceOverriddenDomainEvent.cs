using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>Se movio o cambio UNA ocurrencia. `OriginalStartUtc` la identifica; `NewStartUtc` es a donde fue.</summary>
public sealed record AppointmentOccurrenceOverriddenDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    DateTime OriginalStartUtc,
    DateTime? NewStartUtc,
    Guid ModifiedByUserId,
    DateTime ModifiedAtUtc
) : IDomainEvent;
