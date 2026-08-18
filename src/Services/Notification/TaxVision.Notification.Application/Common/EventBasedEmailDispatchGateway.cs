using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Implementación event-based del <see cref="IEmailDispatchGateway"/> — persiste el
/// <see cref="NotificationLog"/> con su <see cref="NotificationDispatchAttempt"/> y publica
/// <see cref="NotificationsEmailSendRequestedIntegrationEvent"/> hacia Postmaster en la misma
/// transacción (outbox transaccional garantiza al-menos-una-vez). Postmaster despacha material y
/// devuelve el resultado por 5 callbacks (<c>succeeded / failed / bounced / suppressed /
/// provider_not_configured</c>) — ver <c>PostmasterEmail*Consumer</c>.
/// </summary>
/// <remarks>
/// Se registra bajo el feature flag <c>Notification:UsePostmasterDispatch</c>, que es <c>true</c>
/// por default (Postmaster tiene consumidor real y estable). Retirar
/// <see cref="InProcessEmailDispatchGateway"/> junto con el flag mismo queda como trabajo futuro,
/// condicionado a confianza operacional real en un despliegue en producción.
/// </remarks>
public sealed class EventBasedEmailDispatchGateway(
    IIntegrationEventPublisher publisher,
    INotificationLogRepository logRepository,
    IUnitOfWork unitOfWork,
    ILogger<EventBasedEmailDispatchGateway> logger
) : IEmailDispatchGateway
{
    public async Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
    {
        // Idempotencia real (bug encontrado en la verificación E2E de PayFlow): si Wolverine
        // reintenta el consumer que invoca este gateway, sin este chequeo se creaba un
        // NotificationLog/attempt nuevo -- y un segundo correo real -- por cada reintento, pese a
        // que el IdempotencyKey publicado hacia Postmaster ya era estable (ver comentario más abajo).
        // Buscar por RelatedEventId es lo mismo que exige ese IdempotencyKey: estable entre
        // reintentos porque viene del evento de dominio original, no de un Guid recién generado.
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
                    "Email {TemplateKey} for tenant {TenantId} already queued for event {RelatedEventId} (log {LogId}); skipping duplicate dispatch.",
                    request.TemplateKey,
                    request.TenantId,
                    relatedEventId,
                    existing.Id
                );
                return new EmailDispatchResult(
                    existing.Id,
                    lastAttempt?.Id ?? Guid.Empty,
                    lastAttempt?.Status ?? NotificationDispatchAttemptStatus.Queued,
                    lastAttempt?.ProviderMessageId,
                    Error: null
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
                "EventBasedEmailDispatchGateway rejected request for template {TemplateKey}: {Error}",
                request.TemplateKey,
                logCreation.Error.Message
            );
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

        // El log queda en estado Pending y el attempt en Queued hasta que Postmaster devuelva
        // el callback. Nunca invocamos MarkSent aquí — solo el consumer de succeeded lo hará.
        //
        // El chequeo de arriba (GetByRelatedEventIdAsync) ya cubrió el caso "hay un log de un
        // intento anterior para este mismo evento" -- llegar hasta acá significa que este es el
        // primer intento. RelatedEventId sigue siendo la base preferida del IdempotencyKey (en vez
        // de log.Id/attempt.Id recién generados) como defensa en profundidad: si dos réplicas
        // llegaran a correr esta creación en paralelo para el mismo evento (carrera que el chequeo
        // de arriba no cierra del todo, al no tener lock), SqlIdempotencyGuard (Postmaster) todavía
        // puede dedupear el envío real porque ambas calcularían la MISMA key.
        var idempotencyKey =
            request.IdempotencyKey ?? request.RelatedEventId?.ToString("N") ?? $"{log.Id:N}:{attempt.Id:N}";
        var evt = new NotificationsEmailSendRequestedIntegrationEvent
        {
            TenantId = request.TenantId,
            CorrelationId = request.CorrelationId ?? string.Empty,
            NotificationLogId = log.Id,
            DispatchAttemptId = attempt.Id,
            IdempotencyKey = idempotencyKey,
            To = request.To,
            Subject = request.Subject,
            HtmlBody = request.HtmlBody,
            TextBody = request.TextBody,
            TemplateKey = request.TemplateKey,
            RequiredProviderScope = request.Scope.ToString(),
            LogoScope = request.Scope.ToString(),
            Stream = request.Stream.ToString(),
            Cc = request.Cc,
            Bcc = request.Bcc,
            TemplateVariables = SerializeVariables(request.TemplateVariables),
            PriorityHint = request.PriorityHint,
            ReplyToThreadId = request.ReplyToThreadId,
            AttachmentFileIds = request.AttachmentFileIds,
            // Hardening Fase 9: propaga las referencias de logo/CID tal cual — mismo tipo
            // (EmailInlineAssetReference) en ambos lados, sin mapeo, porque el evento es exactamente
            // el contrato que este campo fue diseñado para viajar.
            InlineAssets = request.InlineAssets,
        };

        // PublishAsync + SaveChanges en el mismo scope → outbox transaccional (durable).
        await publisher.PublishAsync(evt, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Email {TemplateKey} queued to Postmaster for tenant {TenantId} (log {LogId}, attempt {AttemptId}).",
            request.TemplateKey,
            request.TenantId,
            log.Id,
            attempt.Id
        );

        return new EmailDispatchResult(
            log.Id,
            attempt.Id,
            NotificationDispatchAttemptStatus.Queued,
            ProviderMessageId: null,
            Error: null
        );
    }

    private static IReadOnlyDictionary<string, string>? SerializeVariables(IReadOnlyDictionary<string, object>? source)
    {
        if (source is null || source.Count == 0)
            return null;
        var result = new Dictionary<string, string>(source.Count, StringComparer.Ordinal);
        foreach (var kv in source)
        {
            result[kv.Key] = kv.Value?.ToString() ?? string.Empty;
        }
        return result;
    }
}
