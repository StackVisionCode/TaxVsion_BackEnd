using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

/// <summary>Canal SMS: envía y persiste el <see cref="NotificationLog"/> (Sent/Failed) en la transacción del consumer.</summary>
public sealed class SmsDispatch(
    ISmsSender smsSender,
    INotificationLogRepository logs,
    DispatchChannelGate gate,
    IUnitOfWork unitOfWork,
    INotificationDispatchMetrics metrics
)
{
    public async Task SendAsync(
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
        if (
            !await gate.AllowsAsync(
                tenantId,
                recipientUserId,
                recipientUserId,
                category,
                NotificationChannel.Sms,
                templateKey,
                ct
            )
        )
            return;

        var recipient = NotificationRecipient.ForPhone(phoneNumber);
        if (recipient.IsFailure)
            return;

        var logResult = NotificationLog.Create(
            tenantId,
            NotificationChannel.Sms,
            recipient.Value,
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
}
