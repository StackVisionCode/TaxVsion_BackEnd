using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Sending.Commands.DispatchSentMessage;
using TaxVision.Postmaster.Application.Sending.Lanes;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace TaxVision.Postmaster.Application.Consumers;

/// <summary>
/// Paso transaccional del envío (PMS-01): recibe la solicitud de Notification, dedupa, resuelve el carril
/// y deja el <see cref="SentMessage"/> en Sending. La reserva, el mensaje y el
/// <see cref="DispatchSentMessageCommand"/> se confirman juntos; el envío real lo hace
/// <see cref="DispatchSentMessageHandler"/>, fuera de la transacción. Dedup adicional por <c>MessageId</c>
/// vía <c>UseDurableInbox</c> de Wolverine (ya activo a nivel de servicio, Fase 1).
/// </summary>
/// <remarks>
/// Desviaciones documentadas respecto al texto literal del plan §Fase 5:
/// <list type="bullet">
/// <item>
/// El provider se resuelve ANTES de <see cref="SentMessage.Queue"/>, no después — <c>ProviderCode</c>
/// y <c>FromAddress</c> son campos requeridos y válidos del aggregate; solo se conocen una vez
/// resuelto el provider, así que no hay forma correcta de "encolar primero, resolver después" sin
/// inventar valores placeholder. Si la resolución falla, no se crea <see cref="SentMessage"/> — el
/// callback de falla se publica directo y la reserva de idempotencia queda sin completar (permite
/// reintento real si el tenant configura su provider antes de que expire la ventana de retry).
/// </item>
/// <item>
/// No hay paso de "resolver template vía Scribe": el evento ya trae <c>HtmlBody</c>/<c>TextBody</c>
/// renderizados por Notification (confirmado en el contrato del evento, Fase Notification 4) —
/// <c>TemplateKey</c> solo se usa para auditoría en <see cref="SentMessage.TemplateKey"/>.
/// </item>
/// <item>
/// Hardening Fase 9 (actualiza el punto anterior): <see cref="Domain.Sending.InlineAsset"/> SÍ se
/// adjuntan hoy, pero no se resuelven por <c>LogoScope</c> acá — vienen ya resueltos por Scribe
/// (vía <c>LogoScope</c> en el momento del render, Scribe Fase 4.5) como referencias dentro de
/// <c>evt.InlineAssets</c>. <see cref="DispatchSentMessageHandler"/> las valida y descarga los bytes
/// reales para los dos carriles.
/// </item>
/// </list>
/// </remarks>
public static class NotificationsEmailSendRequestedConsumer
{
    /// <summary>
    /// Entregas de un email por encima del cupo antes de darlo por fallido. Cada diferido espera la
    /// ventana del limiter (≤60 s) más jitter: 60 entregas cubren cerca de una hora de ráfaga.
    /// </summary>
    public const int MaxRateLimitedDeliveries = 60;

    /// <summary>
    /// PMS-11. Evento malformado: a la cola de errores sin reintentos, y Notification se entera para no quedar en
    /// Pending. El aviso sale desde esta acción y no desde el handler: lo publicado ahí se revierte al lanzar.
    /// </summary>
    public static void Configure(HandlerChain chain) =>
        chain
            .OnException<MalformedEmailEventException>()
            .MoveToErrorQueue()
            .And(NotifyMalformedAsync, "Tell Notification that the malformed email will not be sent");

    private static async ValueTask NotifyMalformedAsync(
        IWolverineRuntime runtime,
        IEnvelopeLifecycle lifecycle,
        Exception failure
    )
    {
        if (
            lifecycle.Envelope?.Message is NotificationsEmailSendRequestedIntegrationEvent evt
            && failure is MalformedEmailEventException malformed
            && MalformedCallback(evt, malformed.ErrorCode) is { } callback
        )
            await new MessageBus(runtime).PublishAsync(callback);
    }

    internal static PostmasterEmailDeliveryFailedIntegrationEvent? MalformedCallback(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        string errorCode
    ) =>
        evt.NotificationLogId == Guid.Empty
            ? null
            : new PostmasterEmailDeliveryFailedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                NotificationLogId = evt.NotificationLogId,
                DispatchAttemptId = evt.DispatchAttemptId,
                SentMessageId = Guid.Empty,
                Reason = $"event.malformed: {errorCode}",
                CampaignId = evt.CampaignId,
                EventAtUtc = DateTime.UtcNow,
            };

    /// <param name="envelope">La entrega actual (la inyecta Wolverine); null en llamadas directas.</param>
    public static async Task Handle(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        IIdempotencyGuard idempotencyGuard,
        IProviderResolver providerResolver,
        IConnectedMailboxResolver mailboxResolver,
        ISuppressionListRepository suppressionList,
        IEmailProviderRateLimiter rateLimiter,
        ISentMessageRepository sentMessages,
        ITenantDirectoryRepository tenantDirectory,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct,
        Envelope? envelope = null
    )
    {
        var delivery = Math.Max(1, envelope?.Attempts ?? 1);
        using (correlation.Push(evt.CorrelationId))
        {
            var reservation = await idempotencyGuard.TryReserveAsync(evt.TenantId, evt.IdempotencyKey, ct);
            switch (reservation.Outcome)
            {
                case IdempotencyReservationOutcome.AlreadyCompleted:
                    await PublishReplayAsync(
                        bus,
                        evt,
                        reservation.ExistingSentMessageId!.Value,
                        sentMessages,
                        logger,
                        ct
                    );
                    return;
                case IdempotencyReservationOutcome.InProgress:
                    // No crear un segundo SentMessage: otro intento concurrente (Notification
                    // republicando, o el mismo evento entregado dos veces at-least-once) todavía está
                    // procesando esta clave. Se lanza para que la política de retry+cooldown de
                    // Wolverine (Program.cs) reintente el mensaje completo — la próxima vuelta verá
                    // AlreadyCompleted si el ganador ya terminó.
                    throw new IdempotencyReservationInProgressException(evt.IdempotencyKey);
                case IdempotencyReservationOutcome.Reserved:
                default:
                    break;
            }

            var lane = new LanePreparation(
                evt,
                suppressionList,
                sentMessages,
                tenantDirectory,
                idempotencyGuard,
                unitOfWork,
                bus,
                logger,
                ct
            );

            // Escalón 1 de la cadena. Se decide acá y no en ProviderResolver porque el canal del buzón conectado no
            // envía por SMTP: no produce un ResolvedEmailProvider y tiene su propio camino de envío,
            // así que la elección es de carril, no de provider.
            var requiredScope = ParseProviderScope(evt.RequiredProviderScope);
            var mailboxResult = requiredScope is ProviderScope.TenantMailbox or ProviderScope.TenantPreferred
                ? await mailboxResolver.ResolveAsync(evt.TenantId, ct)
                : null;

            // TenantMailbox es estricto: sin cuenta conectada falla acá y NO baja a SMTP.
            // TenantPreferred sin cuenta sigue a los escalones 2 y 3.
            if (
                mailboxResult is not null
                && (
                    mailboxResult.Status == MailboxResolutionStatus.Resolved
                    || requiredScope == ProviderScope.TenantMailbox
                )
            )
            {
                await MailboxLaneSender.PrepareAsync(lane, mailboxResult);
                return;
            }

            await SystemLaneSender.PrepareAsync(lane, delivery, providerResolver, rateLimiter);
        }
    }

    /// <summary>
    /// Clave ya cerrada: se repite el resultado guardado (Sent, Failed o Suppressed), no un éxito a ciegas. Sin el
    /// mensaje se mantiene el aviso de éxito de siempre.
    /// </summary>
    private static async Task PublishReplayAsync(
        IMessageBus bus,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        Guid sentMessageId,
        ISentMessageRepository sentMessages,
        ILogger logger,
        CancellationToken ct
    )
    {
        var message = await sentMessages.GetForDispatchAsync(sentMessageId, ct);
        switch (message?.Status)
        {
            case SentMessageStatus.Failed:
                await bus.PublishAsync(
                    new PostmasterEmailDeliveryFailedIntegrationEvent
                    {
                        TenantId = evt.TenantId,
                        CorrelationId = evt.CorrelationId,
                        NotificationLogId = evt.NotificationLogId,
                        DispatchAttemptId = evt.DispatchAttemptId,
                        SentMessageId = sentMessageId,
                        Reason = message.ErrorReason ?? "send.failed",
                        CampaignId = evt.CampaignId,
                        EventAtUtc = DateTime.UtcNow,
                    }
                );
                return;
            case SentMessageStatus.Suppressed:
                await bus.PublishAsync(
                    new PostmasterEmailDeliverySuppressedIntegrationEvent
                    {
                        TenantId = evt.TenantId,
                        CorrelationId = evt.CorrelationId,
                        NotificationLogId = evt.NotificationLogId,
                        DispatchAttemptId = evt.DispatchAttemptId,
                        SentMessageId = sentMessageId,
                        SuppressionReason = message.ErrorReason ?? "recipient.suppressed",
                        CampaignId = evt.CampaignId,
                        EventAtUtc = DateTime.UtcNow,
                    }
                );
                return;
            case SentMessageStatus.Sent:
                var providerMessageId = message
                    .Recipients.Select(r => r.ProviderMessageId)
                    .FirstOrDefault(id => id is not null);
                await PublishSucceededAsync(bus, evt, sentMessageId, providerMessageId, ct);
                return;
            default:
                logger.LogWarning(
                    "Replay of SentMessage {SentMessageId} found it {Status}; reported as succeeded.",
                    sentMessageId,
                    message?.Status.ToString() ?? "missing"
                );
                await PublishSucceededAsync(bus, evt, sentMessageId, providerMessageId: null, ct);
                return;
        }
    }

    internal static ValueTask PublishSucceededAsync(
        IMessageBus bus,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        Guid sentMessageId,
        string? providerMessageId,
        CancellationToken ct
    ) =>
        bus.PublishAsync(
            new PostmasterEmailDeliverySucceededIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                NotificationLogId = evt.NotificationLogId,
                DispatchAttemptId = evt.DispatchAttemptId,
                SentMessageId = sentMessageId,
                ProviderMessageId = providerMessageId,
                CampaignId = evt.CampaignId,
                EventAtUtc = DateTime.UtcNow,
            }
        );

    internal static EmailStream ParseEmailStream(string value) =>
        Enum.TryParse<EmailStream>(value, ignoreCase: true, out var stream) ? stream : EmailStream.Transactional;

    internal static ProviderScope ParseProviderScope(string value) =>
        Enum.TryParse<ProviderScope>(value, ignoreCase: true, out var scope) ? scope : ProviderScope.System;

    internal static ProviderPriorityHint? ParsePriorityHint(string? value) =>
        Enum.TryParse<ProviderPriorityHint>(value, ignoreCase: true, out var hint) ? hint : null;
}
