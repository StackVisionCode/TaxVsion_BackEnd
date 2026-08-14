using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>Un asistente contesto la invitacion. Va al organizador, que es quien decide si mueve la cita.</summary>
public sealed record AppointmentAttendeeRespondedDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    Guid AttendeeId,
    AttendeeResponse Response,
    DateTime RespondedAtUtc
) : IDomainEvent;
