using BuildingBlocks.Persistence;
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
    IEmailSender emailSender,
    ISmsSender smsSender,
    INotificationLogRepository logs,
    IUnitOfWork unitOfWork,
    ILogger<NotificationDispatcher> logger)
{
    public async Task SendEmailAsync(
        Guid tenantId,
        string to,
        RenderedEmail email,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        CancellationToken ct = default)
    {
        var logResult = NotificationLog.Create(
            tenantId, NotificationChannel.Email, to, email.Subject,
            templateKey, relatedEventId, correlationId);
        if (logResult.IsFailure)
        {
            logger.LogError(
                "Invalid notification log for template {TemplateKey}: {Error}",
                templateKey, logResult.Error.Message);
            return;
        }

        var log = logResult.Value;
        await logs.AddAsync(log, ct);

        var sendResult = await emailSender.SendAsync(
            new EmailMessage(to, email.Subject, email.HtmlBody, email.TextBody), ct);
        if (sendResult.IsSuccess)
        {
            log.MarkSent();
            logger.LogInformation(
                "Email {TemplateKey} sent to {Recipient} for tenant {TenantId}.",
                templateKey, to, tenantId);
        }
        else
        {
            log.MarkFailed(sendResult.Error.Message);
            logger.LogError(
                "Email {TemplateKey} to {Recipient} failed for tenant {TenantId}: {Error}",
                templateKey, to, tenantId, sendResult.Error.Message);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task SendSmsAsync(
        Guid tenantId,
        string phoneNumber,
        string text,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        CancellationToken ct = default)
    {
        var logResult = NotificationLog.Create(
            tenantId, NotificationChannel.Sms, phoneNumber, templateKey,
            templateKey, relatedEventId, correlationId);
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

    /// <summary>Registra una notificación in-app (sin envío externo).</summary>
    public async Task RecordInAppAsync(
        Guid tenantId,
        string recipient,
        string subject,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        CancellationToken ct = default)
    {
        var logResult = NotificationLog.Create(
            tenantId, NotificationChannel.InApp, recipient, subject,
            templateKey, relatedEventId, correlationId);
        if (logResult.IsFailure)
            return;

        logResult.Value.MarkSent();
        await logs.AddAsync(logResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
