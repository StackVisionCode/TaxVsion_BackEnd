using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>Se creo una cita.</summary>
public sealed record AppointmentScheduledDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    Guid OrganizerUserId,
    Guid AppointmentTypeId,
    bool IsVirtual,
    DateTime ScheduledAtUtc,
    AttendeeRsvpToken[]? RsvpTokens = null,
    string? BookedByCustomerName = null,
    bool ApprovedRequest = false
) : IDomainEvent;
