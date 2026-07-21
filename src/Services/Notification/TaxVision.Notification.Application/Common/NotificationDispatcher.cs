using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
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
    IUnitOfWork unitOfWork,
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
        CancellationToken ct = default
    )
    {
        if (!await IsAllowedAsync(tenantId, recipientUserId, category, NotificationChannel.Sms, ct))
            return;

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

        var sendResult = await smsSender.SendAsync(phoneNumber, text, ct);
        if (sendResult.IsSuccess)
            log.MarkSent();
        else
            log.MarkFailed(sendResult.Error.Message);

        await unitOfWork.SaveChangesAsync(ct);
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
        if (!await IsAllowedAsync(tenantId, recipientUserId, category, NotificationChannel.Push, ct))
            return Result.Success();

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
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        log.MarkFailed(lastError);
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
        CancellationToken ct = default
    )
    {
        if (!await IsAllowedAsync(tenantId, recipientUserId, category, NotificationChannel.InApp, ct))
            return;

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
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Cuenta y seguridad nunca se filtra (locked). Sin un UserId resuelto no hay preferencia
    /// que consultar — se permite el envío (comportamiento preexistente, sin regresión).
    /// </summary>
    private async Task<bool> IsAllowedAsync(
        Guid tenantId,
        Guid? recipientUserId,
        NotificationCategory category,
        NotificationChannel channel,
        CancellationToken ct
    )
    {
        if (NotificationCategoryRules.IsLocked(category))
            return true;
        if (recipientUserId is null)
            return true;

        return await preferences.IsEnabledAsync(tenantId, recipientUserId.Value, category, channel, ct);
    }
}
