using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>
/// Se cayó una ocurrencia. El aviso a los asistentes sale siempre: quien pierde una reunion de una serie
/// tiene que enterarse. Y además el hecho preciso, que dice cual de las N se cayó.
/// </summary>
public static class AppointmentOccurrenceCancelledDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        AppointmentOccurrenceCancelledDomainEvent cancelled,
        IAppointmentRepository appointments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        var found = await appointments.GetByIdAsync(cancelled.TenantId, cancelled.AppointmentId, ct);
        if (found.IsFailure)
            return messages;

        var correlationId = envelope.CorrelationId ?? string.Empty;
        messages.Add(
            AppointmentEvents.Cancelled(
                found.Value,
                nameof(EditScope.ThisOccurrence),
                cancelled.OriginalStartUtc,
                cancelled.Reason,
                correlationId
            )
        );
        messages.Add(
            new OccurrenceCancelledIntegrationEvent
            {
                TenantId = cancelled.TenantId,
                CorrelationId = correlationId,
                AppointmentId = cancelled.AppointmentId,
                OriginalStartUtc = cancelled.OriginalStartUtc,
            }
        );
        return messages;
    }
}
