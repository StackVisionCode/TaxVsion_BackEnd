using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>Un cliente pidió una cita en el portal; la oficina tiene que aprobarla, asignarla o rechazarla.</summary>
public sealed record AppointmentBookingRequestedDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    Guid OrganizerUserId,
    string? CustomerName,
    DateTime RequestedAtUtc
) : IDomainEvent;
