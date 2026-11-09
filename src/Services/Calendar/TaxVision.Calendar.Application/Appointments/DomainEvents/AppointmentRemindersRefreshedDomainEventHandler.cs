using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>
/// Pide los avisos de la cita con su estado vigente: si después de este evento la cita se canceló, no se pide
/// nada, y si se volvió a mover, el pedido ya sale con la hora nueva.
/// </summary>
public static class AppointmentRemindersRefreshedDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        AppointmentRemindersRefreshedDomainEvent refreshed,
        IAppointmentRepository appointments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        var found = await appointments.GetByIdAsync(refreshed.TenantId, refreshed.AppointmentId, ct);
        if (found.IsFailure)
            return messages;

        messages.AddRange(
            AppointmentReminderTargets.Refresh(
                found.Value,
                refreshed.KeptTargetIds,
                refreshed.LeadChanged,
                refreshed.RefreshedAtUtc,
                envelope.CorrelationId ?? string.Empty
            )
        );
        return messages;
    }
}
