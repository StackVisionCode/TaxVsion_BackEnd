using BuildingBlocks.Results;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Punto único de envío + registro. Cada canal vive en su clase (<see cref="SmsDispatch"/>,
/// <see cref="PushDispatch"/>, <see cref="InAppDispatch"/>) y comparte <see cref="DispatchChannelGate"/>:
/// destinatario inactivo primero, preferencia después. Un fallo de envío NO relanza excepción: queda
/// registrado como Failed para reintento/diagnóstico, evitando reencolar eventos con tokens ya emitidos.
///
/// Fase 5 del plan de notificaciones dinámicas: <c>category</c> es un parámetro obligatorio
/// (no un filtro aparte opcional) — todo llamador declara bajo qué categoría cae su
/// notificación. Así es imposible que un consumer nuevo se "olvide" de respetar la
/// preferencia del usuario, a diferencia de la versión anterior de esta tabla (borrada en
/// Hardening Fase 20 porque ningún consumer la consultaba).
/// </summary>
public sealed class NotificationDispatcher(
    SmsDispatch smsDispatch,
    PushDispatch pushDispatch,
    InAppDispatch inAppDispatch
)
{
    public const string NoDeviceReason = PushDispatch.NoDeviceReason;

    public Task SendSmsAsync(
        Guid tenantId,
        string phoneNumber,
        string text,
        NotificationCategory category,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        Guid? recipientUserId = null,
        string? idempotencyToken = null,
        CancellationToken ct = default
    ) =>
        smsDispatch.SendAsync(
            tenantId,
            phoneNumber,
            text,
            category,
            templateKey,
            relatedEventId,
            correlationId,
            recipientUserId,
            idempotencyToken,
            ct
        );

    /// <inheritdoc cref="PushDispatch.SendAsync"/>
    public Task<Result> SendPushAsync(
        Guid tenantId,
        Guid recipientUserId,
        string title,
        string body,
        NotificationCategory category,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        CancellationToken ct = default
    ) =>
        pushDispatch.SendAsync(
            new PushDispatchRequest(
                tenantId,
                recipientUserId,
                title,
                body,
                category,
                templateKey,
                templateKey,
                relatedEventId,
                correlationId
            ),
            ct
        );

    /// <inheritdoc cref="PushDispatch.SendAsync"/>
    public Task<Result> SendPushAsync(PushDispatchRequest request, CancellationToken ct = default) =>
        pushDispatch.SendAsync(request, ct);

    /// <inheritdoc cref="PushDispatch.DispatchAsync"/>
    public Task<PushDispatchOutcome> DispatchPushAsync(PushDispatchRequest request, CancellationToken ct = default) =>
        pushDispatch.DispatchAsync(request, ct);

    /// <inheritdoc cref="PushDispatch.CanSendAsync"/>
    public Task<bool> CanSendPushAsync(
        Guid tenantId,
        Guid recipientUserId,
        NotificationCategory category,
        string kind,
        CancellationToken ct = default
    ) => pushDispatch.CanSendAsync(tenantId, recipientUserId, category, kind, ct);

    /// <inheritdoc cref="InAppDispatch.RecordAsync"/>
    public Task RecordInAppAsync(
        Guid tenantId,
        NotificationRecipient recipient,
        string subject,
        NotificationCategory category,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        Guid? recipientUserId = null,
        InAppNotificationRequest? inApp = null,
        CancellationToken ct = default
    ) =>
        inAppDispatch.RecordAsync(
            tenantId,
            recipient,
            subject,
            category,
            templateKey,
            relatedEventId,
            correlationId,
            recipientUserId,
            inApp,
            ct
        );
}
