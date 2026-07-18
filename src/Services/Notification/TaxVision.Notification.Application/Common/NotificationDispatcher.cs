using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Punto único de envío + registro: envía por el canal indicado y persiste el
/// NotificationLog (Sent/Failed) en la misma transacción del consumer.
/// Un fallo de envío NO relanza excepción: queda registrado como Failed para
/// reintento/diagnóstico, evitando reencolar eventos con tokens ya emitidos.
/// </summary>
public sealed class NotificationDispatcher(
    ISmsSender smsSender,
    IPushSender pushSender,
    IPushDeviceTokenRepository pushDeviceTokens,
    INotificationLogRepository logs,
    IUnitOfWork unitOfWork,
    ILogger<NotificationDispatcher> logger
)
{
    public async Task SendSmsAsync(
        Guid tenantId,
        string phoneNumber,
        string text,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        CancellationToken ct = default
    )
    {
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
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        CancellationToken ct = default
    )
    {
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

    /// <summary>Registra una notificación in-app (sin envío externo).</summary>
    public async Task RecordInAppAsync(
        Guid tenantId,
        string recipient,
        string subject,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        CancellationToken ct = default
    )
    {
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
}
