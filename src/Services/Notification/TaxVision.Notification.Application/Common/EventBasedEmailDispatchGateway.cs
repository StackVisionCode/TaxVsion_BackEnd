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
/// Introducido en Notifications Fase 4. Se registra bajo el feature flag
/// <c>Notification:UsePostmasterDispatch</c> — desde Hardening Fase 21 (2026-07-18) ese flag es
/// <c>true</c> por default (Postmaster tiene consumidor real y estable). Retirar
/// <see cref="InProcessEmailDispatchGateway"/> junto con el flag mismo queda como trabajo futuro fuera
/// del plan de hardening, condicionado a confianza operacional real en un despliegue en producción.
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
        // BUG (2026-07-19, encontrado por duplicados reales en producción — 4 correos por un solo
        // registro de tenant): este método crea un NotificationLog/DispatchAttempt con GUIDs nuevos
        // en CADA llamada, sin chequear si ya existe uno para el mismo evento de origen. Si Wolverine
        // reintenta el consumer (RetryWithCooldown en Program.cs, hasta 4 intentos totales) por una
        // falla transitoria — típicamente Scribe/auth-api no disponibles al arrancar, exactamente lo
        // que se vio en logs de scribe-api ese día — cada reintento volvía a caer acá y generaba un
        // log.Id/attempt.Id nuevos. Al no pasar IdempotencyKey (ningún consumer de AuthEventConsumers
        // lo hace), la clave de idempotencia caía a esos GUIDs recién generados: distinta en cada
        // intento. SqlIdempotencyGuard (Postmaster) SÍ dedupea correctamente por (TenantId,
        // IdempotencyKey) — el problema nunca fue el guard, fue que cada intento le mandaba una clave
        // distinta, así que cada uno pasaba como "nueva" y se enviaba un correo real de verdad.
        // Fix: preferir RelatedEventId (el EventId del evento de dominio original — UserRegistered,
        // InvitationCreated, etc. — que SÍ es estable entre reintentos porque viene deserializado del
        // mismo payload) antes de caer a GUIDs frescos. Solo los despachos sin evento de origen
        // (ninguno hoy, pero el contrato lo permite) siguen usando el fallback anterior.
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
