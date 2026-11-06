using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>
/// Se creó la cita: aviso a los asistentes y sus recordatorios. Corre después del commit, desde la cola
/// domain-events; lo descriptivo sale de la cita recargada.
/// </summary>
public static class AppointmentScheduledDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        AppointmentScheduledDomainEvent scheduled,
        IAppointmentRepository appointments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        var found = await appointments.GetByIdAsync(scheduled.TenantId, scheduled.AppointmentId, ct);
        if (found.IsFailure)
            return messages;

        var correlationId = envelope.CorrelationId ?? string.Empty;
        messages.Add(AppointmentEvents.Scheduled(found.Value, correlationId));

        // El aviso se pide en el acto; el job de 12 h queda como red de seguridad.
        messages.AddRange(AppointmentReminderRequests.For(found.Value, scheduled.ScheduledAtUtc, correlationId));
        return messages;
    }
}
