using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Appointments.Abstractions;

namespace TaxVision.Calendar.Application.Meetings.Consumers;

/// <summary>
/// Guarda el codigo corto de la sala que creo Communication.
///
/// <para>
/// Es la vuelta del enlace: Calendar publica que agendo una cita virtual, Communication crea la sala y
/// devuelve su codigo. Nunca hubo una llamada HTTP entre los dos, asi que con Communication caido la
/// cita se creo igual y la sala aparece cuando el servicio vuelve.
/// </para>
/// </summary>
public static class MeetingLinkedConsumer
{
    public static async Task Handle(
        MeetingLinkedToAppointmentIntegrationEvent evt,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<MeetingLinkedToAppointmentIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(evt.TenantId, out var tenantId) || !Guid.TryParse(evt.AppointmentId, out var appointmentId))
        {
            logger.LogWarning("Meeting link ignored: tenant or appointment id is not a Guid.");
            return;
        }

        if (!Guid.TryParse(evt.MeetingId, out var meetingId) || string.IsNullOrWhiteSpace(evt.ShortCode))
            return;

        using (
            correlation.Push(string.IsNullOrEmpty(evt.CorrelationId) ? Guid.NewGuid().ToString("N") : evt.CorrelationId)
        )
        {
            var found = await appointments.GetByIdAsync(tenantId, appointmentId, ct);
            if (found.IsFailure)
                return;

            found.Value.LinkMeeting(meetingId, evt.ShortCode);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
