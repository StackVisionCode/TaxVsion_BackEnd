using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Consumers;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Application.Sending.Commands.DispatchSentMessage;
using TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;
using TaxVision.Postmaster.Application.Sending.Commands.ReleaseSendAttempt;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using Wolverine;

namespace TaxVision.Postmaster.Application.Sending.Lanes;

/// <summary>Carril del buzón conectado de la oficina (PMS-10): la preparación y el envío, con su fallback al sistema.</summary>
internal static class MailboxLaneSender
{
    private static readonly TimeSpan DefaultRateLimitRetryAfter = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Sin control de cupo propio a diferencia de <see cref="SystemLaneSender"/>: el rate limit del
    /// canal del buzón conectado ya lo aplica Connectors por (tenant, cuenta) en su M2M de envío (D3 §4.4/§7,
    /// <c>ISendRateLimiter</c>, default 20/min) — duplicarlo acá sería el mismo cupo enforced dos veces
    /// con configuraciones potencialmente divergentes.
    /// </summary>
    public static async Task PrepareAsync(LanePreparation lane, MailboxResolveResult resolveResult)
    {
        if (resolveResult.Status != MailboxResolutionStatus.Resolved)
        {
            await PublishNotConfiguredCallbackAsync(lane);
            return;
        }

        // La proyeccion del buzon no guarda nombre, solo la direccion: el From salia pelado. El nombre
        // de la oficina (comercial si lo tiene) es el del directorio, el mismo que el carril SMTP ya usa.
        var provider = resolveResult.Provider! with
        {
            FromDisplayName = (await lane.TenantDirectory.FindAsync(lane.Event.TenantId, lane.Ct))?.SenderName,
        };

        var message = await lane.PersistAsync(Queue(lane.Event, provider));
        if (message is null || await lane.ApplySuppressionAsync(message))
            return;

        await lane.QueueDispatchAsync(message, provider);
    }

    private static Result<SentMessage> Queue(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ResolvedMailbox provider
    ) =>
        SentMessage.Queue(
            evt.TenantId,
            evt.IdempotencyKey,
            SensitiveNotificationContent.ForStorage(evt.TemplateKey, evt.Subject),
            provider.FromAddress,
            NotificationsEmailSendRequestedConsumer.ParseEmailStream(evt.Stream),
            provider.ProviderCode,
            evt.NotificationLogId,
            evt.CorrelationId,
            provider.FromDisplayName,
            replyTo: null,
            evt.TemplateKey,
            DateTime.UtcNow,
            ProviderScope.TenantMailbox,
            campaignId: evt.CampaignId,
            deliveryLane: DeliveryLane.TenantMailbox,
            dispatchAttemptId: evt.DispatchAttemptId
        );

    /// <summary>
    /// A diferencia del carril SMTP no hay caso SystemProviderMissing/ProviderUnhealthy:
    /// <see cref="MailboxResolutionStatus"/> solo distingue Resolved de ProviderNotConfigured (D3 §4.3) — la
    /// proyección local es la única fuente y no hay "salud" que evaluar ahí, la reserva de idempotencia
    /// también queda sin completar por el mismo motivo que en el camino SMTP.
    /// </summary>
    private static ValueTask PublishNotConfiguredCallbackAsync(LanePreparation lane) =>
        lane.Bus.PublishAsync(
            new PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent
            {
                TenantId = lane.Event.TenantId,
                CorrelationId = lane.Event.CorrelationId,
                NotificationLogId = lane.Event.NotificationLogId,
                DispatchAttemptId = lane.Event.DispatchAttemptId,
                CampaignId = lane.Event.CampaignId,
                EventAtUtc = DateTime.UtcNow,
            }
        );

    /// <summary>Lo que el paso de envío ya tiene listo para el buzón.</summary>
    public sealed record MailboxSend(
        DispatchSentMessageCommand Command,
        SentMessage Message,
        RenderedContent Content,
        IReadOnlyList<InlineAssetBytes> InlineAssets,
        IReadOnlyList<OutboundAttachmentBytes> Attachments,
        int Delivery
    );

    /// <summary>Lo que hace falta para salir por el proveedor del sistema desde la vía buzón.</summary>
    public sealed record SystemLane(
        IProviderResolver Resolver,
        IEmailProviderRateLimiter RateLimiter,
        IEmailSender Sender
    );

    /// <param name="Result">Con qué terminó el envío; hay que registrarlo.</param>
    /// <param name="Fallback">La vía del sistema aplicada al mensaje; null si no hubo.</param>
    public sealed record MailboxOutcome(SendResult Result, MailboxFallback? Fallback);

    /// <summary>
    /// Sin threading en v1 (D3 §11): el evento de Notification no trae identificadores nativos para responder
    /// un hilo. Los inline assets salen igual que por SMTP (<c>MailboxLaneParityTests</c>).
    /// </summary>
    public static async Task<MailboxOutcome> SendAsync(
        MailboxSend send,
        IConnectedMailboxSender mailboxSender,
        SystemLane systemLane,
        ITenantDirectoryRepository tenantDirectory,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        var sendResult = await mailboxSender.SendAsync(
            send.Message,
            send.Content,
            send.Command.Mailbox!,
            inReplyToInternetMessageId: null,
            references: null,
            replyToProviderMessageId: null,
            send.Attachments,
            send.InlineAssets,
            ct
        );

        // Connectors rechazó por su cupo por cuenta (429): no salió, se difiere igual que en SMTP.
        if (!sendResult.Success && sendResult.RetryAfter is { } retryAfter)
            await DeferAsync(bus, send, retryAfter, ct);

        if (!ShouldFallBack(send.Message, send.Command.Request, sendResult))
            return new MailboxOutcome(sendResult, null);

        var outcome = await FallBackToSystemAsync(
            send,
            sendResult.ErrorReason ?? "Office mailbox send failed.",
            systemLane,
            tenantDirectory,
            bus,
            logger,
            ct
        );
        return new MailboxOutcome(outcome.Result ?? sendResult, outcome.Fallback);
    }

    private static bool ShouldFallBack(
        SentMessage message,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        SendResult sendResult
    ) =>
        !sendResult.Success
        && sendResult.RetryAfter is null
        && sendResult.MailboxFailure is { } failure
        && MailboxFallbackPolicy.ShouldFallBack(
            NotificationsEmailSendRequestedConsumer.ParseProviderScope(evt.RequiredProviderScope),
            message.Stream,
            failure
        );

    /// <param name="Fallback">La vía del sistema aplicada al mensaje; null si no se pudo intentar.</param>
    /// <param name="Result">El envío (o el fallo de cupo) por el sistema; null si vale el fallo del buzón.</param>
    private sealed record FallbackOutcome(MailboxFallback? Fallback, SendResult? Result);

    /// <summary>
    /// El buzón de la oficina falló por su culpa (desconectado, proveedor caído…): el mismo correo sale por
    /// el proveedor del sistema en nombre de la oficina, con Reply-To al buzón para que las respuestas le
    /// lleguen. Nunca con un resultado desconocido (<see cref="MailboxSendFailure.OutcomeUnknown"/>).
    /// </summary>
    private static async Task<FallbackOutcome> FallBackToSystemAsync(
        MailboxSend send,
        string mailboxFailureReason,
        SystemLane systemLane,
        ITenantDirectoryRepository tenantDirectory,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        var evt = send.Command.Request;
        var resolved = await systemLane.Resolver.ResolveAsync(
            evt.TenantId,
            ProviderScope.System,
            NotificationsEmailSendRequestedConsumer.ParsePriorityHint(evt.PriorityHint),
            systemFallbackAllowed: false,
            ct
        );
        if (resolved.Status != ProviderResolutionStatus.Resolved)
            return new FallbackOutcome(null, null);

        var system = resolved.Provider!;
        var onBehalfOf = (await tenantDirectory.FindAsync(evt.TenantId, ct))?.SenderName;
        var fallback = new MailboxFallback(
            mailboxFailureReason,
            system.ProviderCode,
            system.FromAddress,
            SystemLaneSender.FromDisplayNameFor(onBehalfOf, system, ProviderScope.System),
            send.Command.Mailbox!.FromAddress,
            DateTime.UtcNow
        );
        if (fallback.ApplyTo(send.Message).IsFailure)
            return new FallbackOutcome(null, null);

        logger.LogInformation(
            "Office mailbox send failed for SentMessage {SentMessageId}; retrying through the system provider.",
            send.Message.Id
        );

        var quota = SystemLaneSender.ResolveStreamQuota(send.Message.Stream, system);
        if (quota is null)
            return new FallbackOutcome(
                fallback,
                DispatchSentMessageHandler.Failed(
                    $"BulkRateLimitNotConfigured: provider '{system.ProviderCode}' has no BulkRateLimitPerMinute configured for the Bulk stream."
                )
            );

        var decision = await systemLane.RateLimiter.AcquireAsync(
            system.ProviderCode,
            evt.TenantId,
            send.Message.Stream,
            quota.Value,
            ct
        );
        if (!decision.Allowed)
        {
            await DeferAsync(bus, send, decision.RetryAfter ?? DefaultRateLimitRetryAfter, ct);
            return new FallbackOutcome(
                fallback,
                DispatchSentMessageHandler.Failed(
                    $"RateLimited: still over the provider quota after {send.Delivery} deliveries."
                )
            );
        }

        var result = await systemLane.Sender.SendAsync(
            send.Message,
            send.Content,
            system,
            send.InlineAssets,
            send.Attachments,
            ct
        );
        return new FallbackOutcome(fallback, result);
    }

    /// <summary>
    /// Nada salió y conviene esperar: se libera el intento y Wolverine reprograma este paso tras la espera.
    /// En la última entrega vuelve sin lanzar y el caller lo da por fallido.
    /// </summary>
    private static async Task DeferAsync(IMessageBus bus, MailboxSend send, TimeSpan retryAfter, CancellationToken ct)
    {
        if (send.Delivery >= NotificationsEmailSendRequestedConsumer.MaxRateLimitedDeliveries)
            return;

        await bus.InvokeAsync(new ReleaseSendAttemptCommand(send.Message.Id), ct);
        throw new EmailRateLimitedException(send.Command.IdempotencyKey, retryAfter);
    }
}
