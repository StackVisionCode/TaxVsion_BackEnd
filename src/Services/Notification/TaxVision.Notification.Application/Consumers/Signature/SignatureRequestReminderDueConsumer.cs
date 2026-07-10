using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume <see cref="SignatureRequestReminderDueIntegrationEvent"/> emitido por el
/// ReminderScheduler de Signature. Compone y envía el correo de reminder usando el
/// template versionado <c>sig.reminder.v1</c>.
/// </summary>
public static class SignatureRequestReminderDueConsumer
{
    private const string TemplateKey = SignatureTemplateCatalog.ReminderKey;

    public static async Task Handle(
        SignatureRequestReminderDueIntegrationEvent evt,
        IEmailSender emailSender,
        INotificationLogRepository logRepository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureRequestReminderDueIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            var template = SignatureTemplateCatalog.Reminder(
                evt.Language == "Es",
                evt.FullName,
                evt.PublicUrl,
                evt.ExpiresAtUtc,
                evt.RemindersSent
            );

            var logResult = NotificationLog.Create(
                evt.TenantId,
                NotificationChannel.Email,
                evt.Email,
                template.Subject,
                TemplateKey,
                evt.EventId,
                correlationId
            );
            if (logResult.IsFailure)
            {
                logger.LogWarning(
                    "Reminder log could not be created for signer {SignerId}: {Error}",
                    evt.SignerId,
                    logResult.Error.Message
                );
                return;
            }

            var log = logResult.Value;
            await logRepository.AddAsync(log, ct);

            var send = await emailSender.SendAsync(
                new EmailMessage(evt.Email, template.Subject, template.Html, template.Text),
                ct
            );
            if (send.IsSuccess)
            {
                log.MarkSent();
                logger.LogInformation(
                    "Reminder {RemindersSent}/3 dispatched to signer {SignerId} for request {RequestId}.",
                    evt.RemindersSent,
                    evt.SignerId,
                    evt.SignatureRequestId
                );
            }
            else
            {
                log.MarkFailed(send.Error.Message);
                logger.LogWarning(
                    "Reminder dispatch failed for signer {SignerId}: {Error}",
                    evt.SignerId,
                    send.Error.Message
                );
            }
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static string ResolveCorrelationId(SignatureRequestReminderDueIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
