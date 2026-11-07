using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Consumers;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.Lanes;

/// <summary>Carril SMTP (PMS-10): resuelve el proveedor, aplica el cupo y deja el mensaje listo para despachar.</summary>
internal static class SystemLaneSender
{
    private static readonly TimeSpan DefaultRateLimitRetryAfter = TimeSpan.FromSeconds(60);

    public static async Task PrepareAsync(
        LanePreparation lane,
        int delivery,
        IProviderResolver providerResolver,
        IEmailProviderRateLimiter rateLimiter
    )
    {
        var evt = lane.Event;
        var resolveResult = await providerResolver.ResolveAsync(
            evt.TenantId,
            NotificationsEmailSendRequestedConsumer.ParseProviderScope(evt.RequiredProviderScope),
            NotificationsEmailSendRequestedConsumer.ParsePriorityHint(evt.PriorityHint),
            SystemFallbackAllowed(evt),
            lane.Ct
        );
        if (resolveResult.Status != ProviderResolutionStatus.Resolved)
        {
            await PublishUnresolvedProviderCallbackAsync(lane, resolveResult);
            return;
        }

        // Solo se consulta cuando el correo acabó saliendo por el sistema: en los otros escalones el
        // From ya es el de la oficina y la consulta sería trabajo tirado en cada envío.
        var onBehalfOf =
            resolveResult.EffectiveScope == ProviderScope.System
                ? (await lane.TenantDirectory.FindAsync(evt.TenantId, lane.Ct))?.SenderName
                : null;

        var message = await lane.PersistAsync(Queue(evt, resolveResult, onBehalfOf));
        if (message is null || await lane.ApplySuppressionAsync(message))
            return;

        if (await ApplyRateLimitAsync(lane, message, delivery, resolveResult.Provider!, rateLimiter))
            return;

        await lane.QueueDispatchAsync(message, mailbox: null);
    }

    /// <summary>Bulk nunca cae al cupo Transactional por defecto — null hasta que un admin lo configure explícitamente.</summary>
    public static int? ResolveStreamQuota(EmailStream stream, ResolvedEmailProvider provider) =>
        stream == EmailStream.Bulk ? provider.BulkRateLimitPerMinute : provider.RateLimitPerMinute;

    /// <summary>
    /// Las dos condiciones que hacen seguro el último escalón de <see cref="ProviderScope.TenantPreferred"/>.
    ///
    /// <para><b>Reply-To</b>: sin él el correo sale con la identidad del sistema y nadie sabe a quién
    /// contestarle — eso sí sería el spoofing silencioso que la política evita. Con él, la cabecera
    /// dice la verdad: lo manda la plataforma, responde a la oficina.</para>
    ///
    /// <para><b>Transaccional</b>: una campaña que cayera acá mandaría su volumen por el dominio de
    /// la plataforma. Si un tenant quema su lista, el que acaba en la blacklist es el remitente del
    /// sistema, y con él el correo transaccional de TODAS las oficinas. Una campaña sin provider
    /// propio falla visible, que es reparable; la reputación de envío quemada no lo es.</para>
    /// </summary>
    public static bool SystemFallbackAllowed(NotificationsEmailSendRequestedIntegrationEvent evt) =>
        !string.IsNullOrWhiteSpace(evt.ReplyTo)
        && NotificationsEmailSendRequestedConsumer.ParseEmailStream(evt.Stream) == EmailStream.Transactional;

    /// <summary>
    /// Solo se anuncia a la oficina cuando el correo acabó saliendo por el sistema EN SU NOMBRE. En
    /// los otros escalones el From ya es el de la oficina y repetirlo sobraría.
    /// </summary>
    public static string? FromDisplayNameFor(
        string? onBehalfOf,
        ResolvedEmailProvider provider,
        ProviderScope effectiveScope
    )
    {
        if (effectiveScope != ProviderScope.System || string.IsNullOrWhiteSpace(onBehalfOf))
            return provider.FromDisplayName;

        var platform = provider.FromDisplayName;
        return string.IsNullOrWhiteSpace(platform) ? onBehalfOf.Trim() : $"{onBehalfOf.Trim()} (via {platform})";
    }

    private static Result<SentMessage> Queue(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ResolveResult resolved,
        string? onBehalfOfDisplayName
    )
    {
        var provider = resolved.Provider!;
        var effectiveScope =
            resolved.EffectiveScope
            ?? NotificationsEmailSendRequestedConsumer.ParseProviderScope(evt.RequiredProviderScope);

        return SentMessage.Queue(
            evt.TenantId,
            evt.IdempotencyKey,
            SensitiveNotificationContent.ForStorage(evt.TemplateKey, evt.Subject),
            provider.FromAddress,
            NotificationsEmailSendRequestedConsumer.ParseEmailStream(evt.Stream),
            provider.ProviderCode,
            evt.NotificationLogId,
            evt.CorrelationId,
            FromDisplayNameFor(onBehalfOfDisplayName, provider, effectiveScope),
            ReplyToFor(evt, effectiveScope),
            evt.TemplateKey,
            DateTime.UtcNow,
            // Lo que pasó, no lo que se pidió: con TenantPreferred el scope del evento no dice por
            // cuál transporte salió, y el historial de envíos existe para responder justo eso.
            effectiveScope,
            correspondenceDraftId: null,
            inReplyToInternetMessageId: null,
            references: null,
            campaignId: evt.CampaignId,
            dispatchAttemptId: evt.DispatchAttemptId
        );
    }

    /// <summary>
    /// El Reply-To solo tiene sentido cuando el From no es ya el de la oficina: en los escalones de
    /// tenant, ponerlo apuntaría a la misma dirección que ya firma el correo.
    /// </summary>
    private static string? ReplyToFor(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ProviderScope effectiveScope
    ) => effectiveScope == ProviderScope.System ? evt.ReplyTo : null;

    /// <summary>
    /// Cupo por (ProviderCode, TenantId, Stream) — <c>provider.RateLimitPerMinute</c>/
    /// <c>BulkRateLimitPerMinute</c> ya vienen resueltos (Fase 3/3.5, Bulk-quota-isolation). Bulk y
    /// Transactional se parten en baldes de Redis separados (ver <see cref="IEmailProviderRateLimiter"/>)
    /// para que una ráfaga de campaña nunca demore un email transaccional del mismo tenant. Bulk sin
    /// <c>BulkRateLimitPerMinute</c> configurado es un fallo de configuración: va a Failed. Con el cupo
    /// agotado el envío se difiere (ver <see cref="DeferOverQuotaAsync"/>) y solo pasa a Failed tras
    /// <see cref="NotificationsEmailSendRequestedConsumer.MaxRateLimitedDeliveries"/> entregas.
    /// </summary>
    private static async Task<bool> ApplyRateLimitAsync(
        LanePreparation lane,
        SentMessage message,
        int delivery,
        ResolvedEmailProvider provider,
        IEmailProviderRateLimiter rateLimiter
    )
    {
        var quota = ResolveStreamQuota(message.Stream, provider);
        if (quota is null)
        {
            await FailAsync(
                lane,
                message,
                $"BulkRateLimitNotConfigured: provider '{provider.ProviderCode}' has no BulkRateLimitPerMinute configured for the Bulk stream."
            );
            return true;
        }

        var decision = await rateLimiter.AcquireAsync(
            provider.ProviderCode,
            lane.Event.TenantId,
            message.Stream,
            quota.Value,
            lane.Ct
        );
        if (decision.Allowed)
            return false;

        await DeferOverQuotaAsync(lane, message, delivery, decision.RetryAfter ?? DefaultRateLimitRetryAfter);

        await FailAsync(lane, message, $"RateLimited: still over the provider quota after {delivery} deliveries.");
        return true;
    }

    /// <summary>
    /// Nada salió al proveedor: se deshace el intento (el <c>SentMessage</c> y la reserva de
    /// idempotencia, igual que el rollback de Correspondence) y se lanza
    /// <see cref="EmailRateLimitedException"/> para que Wolverine reprograme la entrega tras
    /// <paramref name="retryAfter"/>. En la última entrega vuelve sin lanzar y el caller lo da por fallido.
    /// </summary>
    private static async Task DeferOverQuotaAsync(
        LanePreparation lane,
        SentMessage message,
        int delivery,
        TimeSpan retryAfter
    )
    {
        if (delivery >= NotificationsEmailSendRequestedConsumer.MaxRateLimitedDeliveries)
            return;

        var evt = lane.Event;
        lane.SentMessages.Remove(message);
        await lane.IdempotencyGuard.ReleaseAsync(evt.TenantId, evt.IdempotencyKey, lane.Ct);
        await lane.UnitOfWork.SaveChangesAsync(lane.Ct);
        throw new EmailRateLimitedException(evt.IdempotencyKey, retryAfter);
    }

    private static async Task FailAsync(LanePreparation lane, SentMessage message, string reason)
    {
        message.MarkAsFailed(reason, DateTime.UtcNow);
        await lane.IdempotencyGuard.CompleteAsync(lane.Event.TenantId, lane.Event.IdempotencyKey, message.Id, lane.Ct);
        await lane.UnitOfWork.SaveChangesAsync(lane.Ct);
    }

    /// <summary>
    /// Sin provider resuelto no hay <see cref="SentMessage"/> posible (ProviderCode/FromAddress son
    /// requeridos). La reserva de idempotencia queda sin completar a propósito — si el tenant
    /// configura su provider antes de que expire la ventana de retry, un reintento real del mismo
    /// evento puede todavía tener éxito.
    /// </summary>
    private static ValueTask PublishUnresolvedProviderCallbackAsync(LanePreparation lane, ResolveResult resolveResult)
    {
        var evt = lane.Event;
        var now = DateTime.UtcNow;
        if (resolveResult.Status == ProviderResolutionStatus.ProviderNotConfigured)
        {
            return lane.Bus.PublishAsync(
                new PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent
                {
                    TenantId = evt.TenantId,
                    CorrelationId = evt.CorrelationId,
                    NotificationLogId = evt.NotificationLogId,
                    DispatchAttemptId = evt.DispatchAttemptId,
                    CampaignId = evt.CampaignId,
                    EventAtUtc = now,
                }
            );
        }

        var reason =
            resolveResult.Status == ProviderResolutionStatus.SystemProviderMissing
                ? $"SystemProviderMissing: {resolveResult.Reason}"
                : $"ProviderUnhealthy: {resolveResult.Reason}";
        lane.Logger.LogWarning(
            "Could not resolve a provider for NotificationLog {NotificationLogId}: {Reason}",
            evt.NotificationLogId,
            reason
        );
        return lane.Bus.PublishAsync(
            new PostmasterEmailDeliveryFailedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                NotificationLogId = evt.NotificationLogId,
                DispatchAttemptId = evt.DispatchAttemptId,
                SentMessageId = Guid.Empty,
                Reason = reason,
                CampaignId = evt.CampaignId,
                EventAtUtc = now,
            }
        );
    }
}
