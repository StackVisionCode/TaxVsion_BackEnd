using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// <c>calendar.appointment_starting_soon.v1</c> — la cita empieza ya.
///
/// <para>
/// Push e in-app, nunca correo: a quince minutos, un correo llega tarde para lo unico que serviria,
/// que es levantarse de la silla.
/// </para>
/// </summary>
public static class AppointmentStartingSoonConsumer
{
    public static async Task Handle(
        AppointmentStartingSoonIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (evt.AttendeeUserIds.Count == 0)
            return;

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            const string title = "Tu cita empieza pronto";
            foreach (var userId in evt.AttendeeUserIds)
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    $"user:{userId:N}",
                    title,
                    NotificationCategory.Calendar,
                    "calendar.appointment_starting_soon",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: userId,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.CalendarAppointmentStartingSoon,
                        userId,
                        evt.EventId,
                        "calendar.appointment_starting_soon.v1",
                        evt.OccurredOn,
                        title,
                        FallbackBody: "Empieza en unos minutos.",
                        ResourceId: evt.AppointmentId.ToString()
                    ),
                    ct: ct
                );

                await dispatcher.SendPushAsync(
                    evt.TenantId,
                    userId,
                    title,
                    "Empieza en unos minutos.",
                    NotificationCategory.Calendar,
                    "calendar.appointment_starting_soon",
                    evt.EventId,
                    correlation.CorrelationId,
                    ct
                );
            }
        }
    }
}
