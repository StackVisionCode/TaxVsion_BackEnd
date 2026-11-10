using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>Se sumo un asistente.</summary>
public sealed record AppointmentAttendeeInvitedDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    Guid AttendeeId,
    AttendeeKind Kind,
    DateTime InvitedAtUtc,
    Guid? UserId,
    Guid? CustomerId,
    string? Email,
    string? RsvpToken = null
) : IDomainEvent;
