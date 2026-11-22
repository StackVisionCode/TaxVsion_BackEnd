using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// Aviso in-app al cliente en su portal sobre una cita suya (confirmada o rechazada). Viaja por el
/// socket, así la pantalla del portal se refresca sola. Sin cuenta de portal activa no hace nada.
/// </summary>
public static class CalendarClientNotices
{
    public static async Task SendAsync(
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        Guid tenantId,
        Guid customerId,
        Guid appointmentId,
        Guid eventId,
        string kind,
        string title,
        DateTime occurredOn,
        string? correlationId,
        CancellationToken ct
    )
    {
        var portalUserId = await CustomerPortalRecipients.ResolveActivePortalUserAsync(
            tenantId,
            customerId,
            resolver,
            ct
        );
        if (portalUserId is not { } userId)
            return;

        await dispatcher.DispatchAsync(
            new NotificationIntent(
                kind,
                tenantId,
                new NotificationTarget(userId, CustomerId: customerId),
                eventId,
                kind,
                occurredOn,
                correlationId,
                InApp: new InAppContent(
                    ResourceId: appointmentId.ToString(),
                    ResourceContext: new Dictionary<string, string> { ["customerId"] = customerId.ToString() },
                    FallbackTitle: title
                )
            ),
            ct
        );
    }
}
