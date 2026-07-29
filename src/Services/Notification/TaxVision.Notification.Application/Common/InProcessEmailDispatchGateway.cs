using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Implementación in-process del <see cref="IEmailDispatchGateway"/> — enviar via SMTP y persistir el
/// <see cref="NotificationLog"/> con su <see cref="NotificationDispatchAttempt"/> en la misma
/// transacción del consumer. Comportamiento observable idéntico al de
/// <c>NotificationDispatcher.SendEmailAsync</c> pre-Fase 3 (se preserva para la suite de smoke
/// tests de baseline).
/// </summary>
/// <remarks>
/// <para>
/// Reemplazada como DEFAULT por <c>EventBasedEmailDispatchGateway</c> bajo el feature flag
/// <c>Notification:UsePostmasterDispatch</c>, que es <c>true</c> por default — esta clase solo se
/// registra en DI cuando el flag se overridea explícitamente a <c>false</c> (rollback operacional).
/// Se mantiene viva a propósito como fallback; eliminarla es trabajo futuro, condicionado a
/// confianza operacional real en producción.
/// </para>
/// <para>
/// Decisión de diseño: la creación del <c>NotificationLog</c> vive AQUÍ (no en el consumer). El plan
/// original sugería que el consumer cree el log antes de invocar el gateway; en la práctica se
/// eligió el approach opuesto para minimizar el diff en los 9 consumers migrados en Fase 3 y
/// preservar la transacción única. En Fase 4, cuando el gateway publica un evento, el gateway sigue
/// siendo el dueño de crear el log — así el <c>NotificationLogId</c> viaja como CorrelationId opaca
/// dentro del evento y Postmaster lo devuelve en los callbacks.
/// </para>
/// </remarks>
public sealed class InProcessEmailDispatchGateway(
    IEmailSender emailSender,
    INotificationLogRepository logRepository,
    IUnitOfWork unitOfWork,
    ILogger<InProcessEmailDispatchGateway> logger
) : IEmailDispatchGateway
{
    public async Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
    {
        // Idempotencia real (mismo bug encontrado en EventBasedEmailDispatchGateway durante la
        // verificación E2E de PayFlow, ver comentario ahí) -- acá es más grave todavía porque este
        // gateway manda el SMTP directo, sin ningún guard de idempotencia downstream (a diferencia
        // de Postmaster/SqlIdempotencyGuard en el otro path): sin este chequeo, un reintento de
        // Wolverine reenviaría el correo real, no solo duplicaría una fila de log.
        if (request.RelatedEventId is { } relatedEventId)
        {
            var existing = await logRepository.GetByRelatedEventIdAsync(
                request.TenantId,
                relatedEventId,
                request.TemplateKey,
                ct
            );
            if (existing is not null)
            {
                var lastAttempt = existing.Attempts.OrderByDescending(a => a.QueuedAtUtc).FirstOrDefault();
                logger.LogInformation(
                    "Email {TemplateKey} for tenant {TenantId} already dispatched for event {RelatedEventId} (log {LogId}); skipping duplicate send.",
                    request.TemplateKey,
                    request.TenantId,
                    relatedEventId,
                    existing.Id
                );
                return new EmailDispatchResult(
                    existing.Id,
                    lastAttempt?.Id ?? Guid.Empty,
                    lastAttempt?.Status ?? NotificationDispatchAttemptStatus.Sent,
                    lastAttempt?.ProviderMessageId,
                    Error: existing.Status == NotificationStatus.Failed ? existing.Error : null
                );
            }
        }

        var logCreation = NotificationLog.Create(
            request.TenantId,
            NotificationChannel.Email,
            request.To,
            request.Subject,
            request.TemplateKey,
            request.RelatedEventId,
            request.CorrelationId
        );
        if (logCreation.IsFailure)
        {
            logger.LogError(
                "InProcessEmailDispatchGateway rejected request for template {TemplateKey}: {Error}",
                request.TemplateKey,
                logCreation.Error.Message
            );
            // Sin log persistido no podemos devolver ids reales; devolvemos vacíos con Failed.
            return new EmailDispatchResult(
                Guid.Empty,
                Guid.Empty,
                NotificationDispatchAttemptStatus.Failed,
                ProviderMessageId: null,
                Error: logCreation.Error.Message
            );
        }

        var log = logCreation.Value;
        var attempt = log.AddDispatchAttempt(NotificationChannel.Email);
        await logRepository.AddAsync(log, ct);

        var sendResult = await emailSender.SendAsync(
            new EmailMessage(request.To, request.Subject, request.HtmlBody, request.TextBody),
            ct
        );

        NotificationDispatchAttemptStatus finalStatus;
        if (sendResult.IsSuccess)
        {
            finalStatus = NotificationDispatchAttemptStatus.Sent;
            log.UpdateAttemptStatus(attempt.Id, finalStatus);
            log.MarkSent();
            logger.LogInformation(
                "Email {TemplateKey} sent to {Recipient} for tenant {TenantId} (log {LogId}).",
                request.TemplateKey,
                request.To,
                request.TenantId,
                log.Id
            );
        }
        else
        {
            finalStatus = NotificationDispatchAttemptStatus.Failed;
            log.UpdateAttemptStatus(attempt.Id, finalStatus, errorReason: sendResult.Error.Message);
            log.MarkFailed(sendResult.Error.Message);
            logger.LogError(
                "Email {TemplateKey} to {Recipient} failed for tenant {TenantId} (log {LogId}): {Error}",
                request.TemplateKey,
                request.To,
                request.TenantId,
                log.Id,
                sendResult.Error.Message
            );
        }

        await unitOfWork.SaveChangesAsync(ct);

        return new EmailDispatchResult(
            log.Id,
            attempt.Id,
            finalStatus,
            ProviderMessageId: null,
            Error: sendResult.IsFailure ? sendResult.Error.Message : null
        );
    }
}
