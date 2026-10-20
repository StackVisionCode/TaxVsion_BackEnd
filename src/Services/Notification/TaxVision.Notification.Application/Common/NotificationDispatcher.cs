using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Punto único de envío + registro: envía por el canal indicado y persiste el
/// NotificationLog (Sent/Failed) en la misma transacción del consumer.
/// Un fallo de envío NO relanza excepción: queda registrado como Failed para
/// reintento/diagnóstico, evitando reencolar eventos con tokens ya emitidos.
///
/// Fase 5 del plan de notificaciones dinámicas: <c>category</c> es un parámetro obligatorio
/// (no un filtro aparte opcional) — todo llamador declara bajo qué categoría cae su
/// notificación, y este dispatcher consulta <see cref="IUserNotificationPreferenceRepository"/>
/// antes de despachar. Así es imposible que un consumer nuevo se "olvide" de respetar la
/// preferencia del usuario, a diferencia de la versión anterior de esta tabla (borrada en
/// Hardening Fase 20 porque ningún consumer la consultaba).
/// </summary>
public sealed class NotificationDispatcher(
    ISmsSender smsSender,
    IPushSender pushSender,
    IPushDeviceTokenRepository pushDeviceTokens,
    INotificationLogRepository logs,
    IUserNotificationPreferenceRepository preferences,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork,
    INotificationDispatchMetrics metrics,
    ILogger<NotificationDispatcher> logger
)
{
    public async Task SendSmsAsync(
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
    )
    {
        if (!await IsAllowedAsync(tenantId, recipientUserId, category, NotificationChannel.Sms, ct, templateKey))
        {
            metrics.RecordSuppressedByPreference(templateKey, "Sms");
            return;
        }

        var logResult = NotificationLog.Create(
            tenantId,
            NotificationChannel.Sms,
            phoneNumber,
            templateKey,
            templateKey,
            relatedEventId,
            correlationId
        );
        if (logResult.IsFailure)
            return;

        var log = logResult.Value;
        await logs.AddAsync(log, ct);

        // El idempotencyToken debe ser estable e independiente del texto cuando el caller
        // trabaja con contenido sensible (OTP). Si no viene, derivamos uno del relatedEventId
        // o del par (templateKey|recipientUserId) para no caer al patrón legacy por texto.
        var effectiveToken = ResolveIdempotencyToken(idempotencyToken, relatedEventId, templateKey, recipientUserId);
        var sendResult = await smsSender.SendAsync(phoneNumber, text, ct, templateKey, effectiveToken);
        if (sendResult.IsSuccess)
        {
            log.MarkSent();
            metrics.RecordSent(templateKey, "Sms");
        }
        else
        {
            log.MarkFailed(sendResult.Error.Message);
            metrics.RecordFailed(templateKey, "Sms");
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static string? ResolveIdempotencyToken(
        string? explicitToken,
        Guid? relatedEventId,
        string? templateKey,
        Guid? recipientUserId
    )
    {
        if (!string.IsNullOrWhiteSpace(explicitToken))
            return explicitToken;
        if (relatedEventId is { } eventId && eventId != Guid.Empty)
            return eventId.ToString("N");
        if (!string.IsNullOrWhiteSpace(templateKey) && recipientUserId is { } user && user != Guid.Empty)
            return $"{templateKey}:{user:N}";
        return null;
    }

    /// <summary>
    /// Envía a TODOS los dispositivos activos del usuario (fan-out best-effort:
    /// un solo dispositivo entregado ya cuenta como Sent — mismo criterio que
    /// el multicast nativo de FCM/APNs, para no perder el aviso por un
    /// dispositivo viejo/desinstalado). Falla con `Notification.NoPushDevices`
    /// si el usuario no tiene ningún token registrado.
    /// </summary>
    public async Task<Result> SendPushAsync(
        Guid tenantId,
        Guid recipientUserId,
        string title,
        string body,
        NotificationCategory category,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        CancellationToken ct = default
    )
    {
        if (!await IsAllowedAsync(tenantId, recipientUserId, category, NotificationChannel.Push, ct, templateKey))
        {
            metrics.RecordSuppressedByPreference(templateKey, "Push");
            return Result.Success();
        }

        var logResult = NotificationLog.Create(
            tenantId,
            NotificationChannel.Push,
            recipientUserId.ToString(),
            title,
            templateKey,
            relatedEventId,
            correlationId
        );
        if (logResult.IsFailure)
        {
            logger.LogError(
                "Invalid notification log for template {TemplateKey}: {Error}",
                templateKey,
                logResult.Error.Message
            );
            return Result.Failure(logResult.Error);
        }

        var log = logResult.Value;
        await logs.AddAsync(log, ct);

        var devices = await pushDeviceTokens.ListActiveForUserAsync(tenantId, recipientUserId, ct);
        if (devices.Count == 0)
        {
            var error = new Error("Notification.NoPushDevices", "Recipient has no registered push devices.");
            log.MarkFailed(error.Message);
            metrics.RecordNoRecipient(templateKey, "Push");
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure(error);
        }

        var anySucceeded = false;
        var lastError = "All device deliveries failed.";
        foreach (var device in devices)
        {
            var sendResult = await pushSender.SendAsync(
                new PushMessage(device.Token, device.Platform, title, body),
                ct
            );
            if (sendResult.IsSuccess)
            {
                anySucceeded = true;
            }
            else
            {
                lastError = sendResult.Error.Message;
                logger.LogWarning(
                    "Push to device {DeviceId} for user {UserId} failed: {Error}",
                    device.Id,
                    recipientUserId,
                    sendResult.Error.Message
                );

                // Fase 7 — el proveedor (FcmPushSender) confirma que el token ya no es
                // entregable (app desinstalada/token expirado). Se revoca acá, no en el sender,
                // porque solo el dispatcher tiene el device.Id resuelto de la proyección local.
                if (sendResult.Error.Code == PushErrorCodes.TokenInvalid)
                    await pushDeviceTokens.RevokeAsync(tenantId, device.Id, ct);
            }
        }

        if (anySucceeded)
        {
            log.MarkSent();
            metrics.RecordSent(templateKey, "Push");
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        log.MarkFailed(lastError);
        metrics.RecordFailed(templateKey, "Push");
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Failure(new Error("Notification.PushFailed", lastError));
    }

    /// <summary>
    /// Registra una notificación in-app (sin envío externo). <paramref name="recipientUserId"/>
    /// es opcional porque <paramref name="recipient"/> a veces no es un usuario real (ej. un
    /// invitado a un meeting por email, sin cuenta) — sin un UserId no hay preferencia que
    /// consultar, así que se despacha igual (mismo comportamiento que antes de esta fase).
    /// </summary>
    public async Task RecordInAppAsync(
        Guid tenantId,
        string recipient,
        string subject,
        NotificationCategory category,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        Guid? recipientUserId = null,
        InAppNotificationRequest? inApp = null,
        CancellationToken ct = default
    )
    {
        var inAppKind = inApp?.Kind ?? templateKey;
        if (!await IsAllowedAsync(tenantId, recipientUserId, category, NotificationChannel.InApp, ct, inAppKind))
        {
            metrics.RecordSuppressedByPreference(inAppKind, "InApp");
            return;
        }

        var logResult = NotificationLog.Create(
            tenantId,
            NotificationChannel.InApp,
            recipient,
            subject,
            templateKey,
            relatedEventId,
            correlationId
        );
        if (logResult.IsFailure)
            return;

        logResult.Value.MarkSent();
        await logs.AddAsync(logResult.Value, ct);

        if (TryBuildInAppEvent(tenantId, correlationId, inApp, out var evt))
            await publisher.PublishAsync(evt!, ct);

        await unitOfWork.SaveChangesAsync(ct);
        // InApp considerado "sent" al publicar el integration event — el consumer Node persistirá la
        // fila y emitirá el socket. Un fallo real de Node queda fuera del scope de este contador
        // (sería un contador propio en Communication).
        metrics.RecordSent(inAppKind, "InApp");
    }

    private bool TryBuildInAppEvent(
        Guid tenantId,
        string? correlationId,
        InAppNotificationRequest? inApp,
        out InAppNotificationRequestedIntegrationEvent? evt
    )
    {
        evt = null;
        if (inApp is null)
            return false;
        if (!NotificationCatalog.TryGet(inApp.Kind, out var entry) || entry is null)
        {
            logger.LogWarning("In-app notification kind {Kind} is not in the catalog; nothing published.", inApp.Kind);
            return false;
        }

        // Trunca antes de publicar: el consumer Node acepta hasta estos limites
        // tras trim; pasar un string mas largo o un cuerpo vacio mandaba la fila
        // directo a DLQ y se perdia la notificacion.
        var fallbackBody = NormalizeOrNull(inApp.FallbackBody, maxLength: 1000);
        evt = new InAppNotificationRequestedIntegrationEvent
        {
            TenantId = tenantId,
            CorrelationId = Truncate(correlationId, 64) ?? string.Empty,
            OccurredOn = DateTime.UtcNow,
            RecipientUserId = inApp.RecipientUserId,
            Category = NotificationContractNames.For(entry.Category),
            Kind = Truncate(entry.Kind, 64) ?? entry.Kind,
            Priority = NotificationContractNames.For(entry.DefaultPriority),
            TitleKey = Truncate(entry.TitleKey, 160) ?? entry.TitleKey,
            BodyKey = Truncate(entry.BodyKey, 160),
            Params = new Dictionary<string, string>(inApp.Params ?? EmptyMap),
            FallbackTitle = Truncate(inApp.FallbackTitle, 200) ?? inApp.FallbackTitle,
            FallbackBody = fallbackBody,
            ActorUserId = inApp.ActorUserId,
            ActorDisplayName = Truncate(inApp.ActorDisplayName, 160),
            ResourceType = NotificationContractNames.For(entry.ResourceType),
            ResourceId = Truncate(inApp.ResourceId, 128),
            ResourceContext = new Dictionary<string, string>(inApp.ResourceContext ?? EmptyMap),
            Action = NotificationContractNames.For(entry.Action),
            SourceEventId = inApp.SourceEventId,
            SourceEventType = Truncate(inApp.SourceEventType, 128) ?? inApp.SourceEventType,
            GroupKey = Truncate(inApp.GroupKey, 256),
            OccurredAtUtc = DateTime.SpecifyKind(inApp.OccurredAtUtc, DateTimeKind.Utc),
        };
        return true;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyMap = new Dictionary<string, string>();

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];

    // Cuerpo vacio tras trim equivale a "no body": el consumer caera a fallbackTitle
    // y Notification.create no fallara por MissingBody.
    private static string? NormalizeOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    /// <summary>
    /// Cuenta y seguridad nunca se filtra (locked). Sin un UserId resuelto no hay preferencia
    /// que consultar — se permite el envío (comportamiento preexistente, sin regresión).
    ///
    /// <para>
    /// <b>Cuando el gate apaga un envío deja rastro.</b> Antes no escribía nada: ni log ni fila en
    /// <c>NotificationLogs</c> — apagaba en silencio. Con eso, un «no me llegan los recordatorios»
    /// era literalmente indistinguible de un bug de entrega, porque no había dónde mirar. Se
    /// registra en <c>Information</c> y no en <c>Debug</c> a propósito: es el <b>resultado</b> de una
    /// decisión del usuario, no ruido de diagnóstico, y es lo que soporte necesita leer en el log del
    /// request que ya está mirando.
    /// </para>
    /// </summary>
    private async Task<bool> IsAllowedAsync(
        Guid tenantId,
        Guid? recipientUserId,
        NotificationCategory category,
        NotificationChannel channel,
        CancellationToken ct,
        string? templateKey = null
    )
    {
        if (NotificationCategoryRules.IsLocked(category))
            return true;
        // D2: lo marcado IsMandatory en el catálogo nunca lo apaga la preferencia del
        // usuario — la capacidad (resolver de audiencia) ya se validó aguas arriba.
        if (
            !string.IsNullOrWhiteSpace(templateKey)
            && NotificationCatalog.TryGet(templateKey, out var catalogEntry)
            && catalogEntry is { IsMandatory: true, AllowedChannels: var allowed }
            && allowed.Contains(channel)
        )
            return true;
        if (recipientUserId is null)
            return true;

        var enabled = await preferences.IsEnabledAsync(tenantId, recipientUserId.Value, category, channel, ct);
        if (!enabled)
        {
            logger.LogInformation(
                "Notification suppressed by user preference: tenant {TenantId}, user {UserId}, "
                    + "category {Category}, channel {Channel}.",
                tenantId,
                recipientUserId.Value,
                category,
                channel
            );
        }

        return enabled;
    }
}
