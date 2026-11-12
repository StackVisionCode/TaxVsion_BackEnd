using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>Se sumó un asistente. Quién es sale del evento; los datos de la cita, de la recarga.</summary>
public static class AppointmentAttendeeInvitedDomainEventHandler
{
    public static async Task<AppointmentAttendeeAddedIntegrationEvent?> Handle(
        AppointmentAttendeeInvitedDomainEvent invited,
        IAppointmentRepository appointments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(invited.TenantId, invited.AppointmentId, ct);
        if (found.IsFailure)
            return null;

        var appointment = found.Value;
        var attendee = appointment.Attendees.FirstOrDefault(a => a.Id == invited.AttendeeId);
        return new AppointmentAttendeeAddedIntegrationEvent
        {
            TenantId = invited.TenantId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            AppointmentId = invited.AppointmentId,
            AttendeeKind = invited.Kind.ToString(),
            UserId = invited.UserId,
            CustomerId = invited.CustomerId,
            Email = invited.Email,
            DisplayName = attendee?.Snapshot.DisplayName,
            RsvpToken = invited.RsvpToken,
            Title = appointment.Title.Value,
            StartUtc = AppointmentEvents.FirstStartOf(appointment),
            TimeZoneId = appointment.Timing.TimeZone.Id,
            IsRecurring = appointment.IsRecurring,
            IsVirtual = appointment.IsVirtual,
        };
    }
}
