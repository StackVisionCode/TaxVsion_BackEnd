using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>La cita se movio. El instante anterior viaja porque Reminder tiene que mover el aviso y Notification tiene que decir de cuando a cuando.</summary>
public sealed record AppointmentRescheduledDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    DateTime? PreviousStartUtc,
    DateTime? NewStartUtc,
    Guid MovedByUserId,
    DateTime MovedAtUtc
) : IDomainEvent;
