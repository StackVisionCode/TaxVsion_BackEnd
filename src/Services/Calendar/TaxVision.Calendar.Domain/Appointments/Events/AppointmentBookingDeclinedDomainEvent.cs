using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>La oficina rechazó la solicitud: el horario se libera y el cliente se entera del motivo.</summary>
public sealed record AppointmentBookingDeclinedDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    string? Reason,
    Guid DeclinedByUserId,
    DateTime DeclinedAtUtc
) : IDomainEvent;
