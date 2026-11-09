using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// Correo e in-app de los asistentes de una cita. El gate de entrega del gateway (inactivo, preferencia,
/// catálogo) decide por cada asistente con <c>UserId</c>; un cliente invitado sin cuenta recibe igual.
/// </summary>
internal static class CalendarRecipients
{
    /// <summary>Un compañero (con <c>UserId</c>) lee como Staff; un cliente o invitado sin cuenta, como Client.</summary>
    public static ScribeRenderAudience AudienceOf(AppointmentRecipient recipient) =>
        recipient.UserId is null ? ScribeRenderAudience.Client : ScribeRenderAudience.Staff;

    /// <summary>
    /// Renderiza una vez por audiencia presente y encola el correo de cada destinatario. Todos los renders
    /// van antes de encolar: si Scribe falla, el reintento no deja correos a medias.
    /// </summary>
    public static async Task QueueByAudienceAsync(
        IReadOnlyList<AppointmentRecipient> recipients,
        Guid tenantId,
        string templateKey,
        string kind,
        IReadOnlyDictionary<string, object?> variables,
        IScribeRenderClient scribeClient,
        IEmailDispatchGateway gateway,
        Guid relatedEventId,
        string? correlationId,
        CancellationToken ct
    )
    {
        var renders = new Dictionary<ScribeRenderAudience, ScribeRenderedEmail>();
        foreach (var recipient in recipients)
        {
            var audience = AudienceOf(recipient);
            if (!renders.ContainsKey(audience))
                renders[audience] = (
                    await scribeClient.RenderAsync(templateKey, tenantId, variables, audience, ct)
                ).EnsureRendered(templateKey);
        }

        foreach (var recipient in recipients)
        {
            await gateway.QueueEmailAsync(
                renders[AudienceOf(recipient)]
                    .ToDispatchRequest(
                        tenantId: tenantId,
                        to: recipient.Email,
                        templateKey: templateKey,
                        relatedEventId: relatedEventId,
                        correlationId: correlationId
                    ) with
                {
                    RecipientUserId = recipient.UserId,
                    Category = NotificationCategory.Calendar,
                    NotificationKind = kind,
                },
                ct
            );
        }
    }

    /// <summary>
    /// Publica in-app por el puente del catálogo a cada asistente con cuenta. Un asistente sin
    /// <c>UserId</c> (cliente invitado) no tiene bandeja interna: queda cubierto por el email.
    /// </summary>
    public static async Task PublishInAppAsync(
        NotificationDispatcher dispatcher,
        IReadOnlyList<AppointmentRecipient> recipients,
        Guid tenantId,
        Guid appointmentId,
        Guid sourceEventId,
        string kind,
        string sourceEventType,
        DateTime occurredAtUtc,
        string title,
        string? correlationId,
        Guid? customerId,
        CancellationToken ct
    )
    {
        var context = new Dictionary<string, string>();
        if (customerId is { } cid)
            context["customerId"] = cid.ToString();

        foreach (var recipient in recipients)
        {
            if (recipient.UserId is not { } userId)
                continue;

            await dispatcher.RecordInAppAsync(
                tenantId,
                NotificationRecipient.ForUser(userId),
                title,
                NotificationCategory.Calendar,
                kind,
                sourceEventId,
                correlationId,
                recipientUserId: userId,
                inApp: new InAppNotificationRequest(
                    kind,
                    userId,
                    sourceEventId,
                    sourceEventType,
                    occurredAtUtc,
                    title,
                    ResourceId: appointmentId.ToString(),
                    ResourceContext: context
                ),
                ct: ct
            );
        }
    }
}
