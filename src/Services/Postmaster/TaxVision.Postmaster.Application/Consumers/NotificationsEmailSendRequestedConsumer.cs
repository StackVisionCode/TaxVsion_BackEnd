using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Sending.Commands.DispatchSentMessage;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using Wolverine;

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

    private static readonly TimeSpan DefaultRateLimitRetryAfter = TimeSpan.FromSeconds(60);

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
                    await PublishSucceededAsync(
                        bus,
                        evt,
                        reservation.ExistingSentMessageId!.Value,
                        providerMessageId: null,
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
                await HandleConnectedMailboxPathAsync(
                    evt,
                    mailboxResult,
                    suppressionList,
                    sentMessages,
                    tenantDirectory,
                    idempotencyGuard,
                    unitOfWork,
                    bus,
                    ct
                );
                return;
            }

            await HandleSmtpPathAsync(
                evt,
                delivery,
                providerResolver,
                suppressionList,
                rateLimiter,
                sentMessages,
                tenantDirectory,
                idempotencyGuard,
                unitOfWork,
                bus,
                logger,
                ct
            );
        }
    }

    private static async Task HandleSmtpPathAsync(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        int delivery,
        IProviderResolver providerResolver,
        ISuppressionListRepository suppressionList,
        IEmailProviderRateLimiter rateLimiter,
        ISentMessageRepository sentMessages,
        ITenantDirectoryRepository tenantDirectory,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        var resolveResult = await providerResolver.ResolveAsync(
            evt.TenantId,
            ParseProviderScope(evt.RequiredProviderScope),
            ParsePriorityHint(evt.PriorityHint),
            SystemFallbackAllowed(evt),
            ct
        );
        if (resolveResult.Status != ProviderResolutionStatus.Resolved)
        {
            await PublishUnresolvedProviderCallbackAsync(bus, evt, resolveResult, logger, ct);
            return;
        }

        // Solo se consulta cuando el correo acabó saliendo por el sistema: en los otros escalones el
        // From ya es el de la oficina y la consulta sería trabajo tirado en cada envío.
        var onBehalfOf =
            resolveResult.EffectiveScope == ProviderScope.System
                ? (await tenantDirectory.FindAsync(evt.TenantId, ct))?.SenderName
                : null;

        var message = await QueueAndPersistAsync(evt, resolveResult, onBehalfOf, sentMessages, unitOfWork, ct);
        if (await ApplySuppressionAsync(message, evt, suppressionList, idempotencyGuard, unitOfWork, ct))
            return;

        if (
            await ApplyRateLimitAsync(
                message,
                evt,
                delivery,
                resolveResult.Provider!,
                rateLimiter,
                sentMessages,
                idempotencyGuard,
                unitOfWork,
                ct
            )
        )
            return;

        await QueueDispatchAsync(message, evt, mailbox: null, unitOfWork, bus, ct);
    }

    /// <summary>
    /// Sin control de cupo propio a diferencia de <see cref="HandleSmtpPathAsync"/>: el rate limit del
    /// canal del buzón conectado ya lo aplica Connectors por (tenant, cuenta) en su M2M de envío (D3 §4.4/§7,
    /// <c>ISendRateLimiter</c>, default 20/min) — duplicarlo acá sería el mismo cupo enforced dos veces
    /// con configuraciones potencialmente divergentes.
    /// </summary>
    private static async Task HandleConnectedMailboxPathAsync(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        MailboxResolveResult resolveResult,
        ISuppressionListRepository suppressionList,
        ISentMessageRepository sentMessages,
        ITenantDirectoryRepository tenantDirectory,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (resolveResult.Status != MailboxResolutionStatus.Resolved)
        {
            await PublishMailboxNotConfiguredCallbackAsync(bus, evt, ct);
            return;
        }

        // La proyeccion del buzon no guarda nombre, solo la direccion: el From salia pelado. El nombre
        // de la oficina (comercial si lo tiene) es el del directorio, el mismo que el carril SMTP ya usa.
        var provider = resolveResult.Provider! with
        {
            FromDisplayName = (await tenantDirectory.FindAsync(evt.TenantId, ct))?.SenderName,
        };

        var message = await QueueAndPersistMailboxAsync(evt, provider, sentMessages, unitOfWork, ct);
        if (await ApplySuppressionAsync(message, evt, suppressionList, idempotencyGuard, unitOfWork, ct))
            return;

        await QueueDispatchAsync(message, evt, provider, unitOfWork, bus, ct);
    }

    /// <summary>
    /// Cierre del paso transaccional: el mensaje queda en Sending y el despacho se publica después de
    /// guardar; Wolverine confirma la reserva, el mensaje y el sobre del comando juntos.
    /// </summary>
    private static async Task QueueDispatchAsync(
        SentMessage message,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ResolvedMailbox? mailbox,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        message.MarkAsSending();
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new DispatchSentMessageCommand(
                message.Id,
                evt.TenantId,
                evt.IdempotencyKey,
                message.DeliveryLane,
                evt,
                mailbox
            )
        );
    }

    /// <summary>
    /// Marca como Suppressed los recipients cuya dirección está en la lista negra del tenant. Si TODOS
    /// quedan suprimidos, el mensaje entero pasa a <see cref="SentMessageStatus.Suppressed"/> sin
    /// intentar el envío y el agregado levanta el aviso (devuelve true — el caller no debe
    /// seguir). Si solo ALGUNOS quedan suprimidos, el envío continúa para el resto — <c>MimeMessageBuilder</c>
    /// excluye los recipients Suppressed del MIME real (Fase 7).
    /// </summary>
    private static async Task<bool> ApplySuppressionAsync(
        SentMessage message,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ISuppressionListRepository suppressionList,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var addresses = message.Recipients.Select(r => r.Address).Distinct().ToList();
        var suppressed = await suppressionList.GetSuppressedAsync(evt.TenantId, addresses, ct);
        if (suppressed.Count == 0)
            return false;

        var now = DateTime.UtcNow;
        foreach (var recipient in message.Recipients.Where(r => suppressed.Contains(r.Address)))
            message.MarkRecipientSuppressed(recipient.Id, "Address in suppression list.", now);

        if (!message.Recipients.All(r => r.Status == RecipientStatus.Suppressed))
        {
            await unitOfWork.SaveChangesAsync(ct);
            return false;
        }

        message.MarkAsSuppressed("All recipients are in the suppression list.", now);
        await idempotencyGuard.CompleteAsync(evt.TenantId, evt.IdempotencyKey, message.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Cupo por (ProviderCode, TenantId, Stream) — <c>provider.RateLimitPerMinute</c>/
    /// <c>BulkRateLimitPerMinute</c> ya vienen resueltos (Fase 3/3.5, Bulk-quota-isolation). Bulk y
    /// Transactional se parten en baldes de Redis separados (ver <see cref="IEmailProviderRateLimiter"/>)
    /// para que una ráfaga de campaña nunca demore un email transaccional del mismo tenant. Bulk sin
    /// <c>BulkRateLimitPerMinute</c> configurado es un fallo de configuración: va a Failed. Con el cupo
    /// agotado el envío se difiere (ver <see cref="DeferOverQuotaAsync"/>) y solo pasa a Failed tras
    /// <see cref="MaxRateLimitedDeliveries"/> entregas.
    /// </summary>
    private static async Task<bool> ApplyRateLimitAsync(
        SentMessage message,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        int delivery,
        ResolvedEmailProvider provider,
        IEmailProviderRateLimiter rateLimiter,
        ISentMessageRepository sentMessages,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var quota = ResolveStreamQuota(message.Stream, provider);
        if (quota is null)
        {
            await FailAsync(
                message,
                evt,
                $"BulkRateLimitNotConfigured: provider '{provider.ProviderCode}' has no BulkRateLimitPerMinute configured for the Bulk stream.",
                idempotencyGuard,
                unitOfWork,
                ct
            );
            return true;
        }

        var decision = await rateLimiter.AcquireAsync(
            provider.ProviderCode,
            evt.TenantId,
            message.Stream,
            quota.Value,
            ct
        );
        if (decision.Allowed)
            return false;

        var retryAfter = decision.RetryAfter ?? DefaultRateLimitRetryAfter;
        await DeferOverQuotaAsync(message, evt, delivery, retryAfter, sentMessages, idempotencyGuard, unitOfWork, ct);

        var reason = $"RateLimited: still over the provider quota after {delivery} deliveries.";
        await FailAsync(message, evt, reason, idempotencyGuard, unitOfWork, ct);
        return true;
    }

    /// <summary>
    /// Nada salió al proveedor: se deshace el intento (el <c>SentMessage</c> y la reserva de
    /// idempotencia, igual que el rollback de Correspondence) y se lanza
    /// <see cref="EmailRateLimitedException"/> para que Wolverine reprograme la entrega tras
    /// <paramref name="retryAfter"/>. En la última entrega vuelve sin lanzar y el caller lo da por fallido.
    /// </summary>
    private static async Task DeferOverQuotaAsync(
        SentMessage message,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        int delivery,
        TimeSpan retryAfter,
        ISentMessageRepository sentMessages,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (delivery >= MaxRateLimitedDeliveries)
            return;

        sentMessages.Remove(message);
        await idempotencyGuard.ReleaseAsync(evt.TenantId, evt.IdempotencyKey, ct);
        await unitOfWork.SaveChangesAsync(ct);
        throw new EmailRateLimitedException(evt.IdempotencyKey, retryAfter);
    }

    /// <summary>Bulk nunca cae al cupo Transactional por defecto — null hasta que un admin lo configure explícitamente.</summary>
    internal static int? ResolveStreamQuota(EmailStream stream, ResolvedEmailProvider provider) =>
        stream == EmailStream.Bulk ? provider.BulkRateLimitPerMinute : provider.RateLimitPerMinute;

    private static async Task FailAsync(
        SentMessage message,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        string reason,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        message.MarkAsFailed(reason, now);
        await idempotencyGuard.CompleteAsync(evt.TenantId, evt.IdempotencyKey, message.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static async Task<SentMessage> QueueAndPersistAsync(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ResolveResult resolved,
        string? onBehalfOfDisplayName,
        ISentMessageRepository sentMessages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var provider = resolved.Provider!;
        var effectiveScope = resolved.EffectiveScope ?? ParseProviderScope(evt.RequiredProviderScope);

        var queueResult = SentMessage.Queue(
            evt.TenantId,
            evt.IdempotencyKey,
            SensitiveNotificationContent.ForStorage(evt.TemplateKey, evt.Subject),
            provider.FromAddress,
            ParseEmailStream(evt.Stream),
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
        if (queueResult.IsFailure)
            throw new InvalidOperationException(
                $"Malformed {nameof(NotificationsEmailSendRequestedIntegrationEvent)}: {queueResult.Error.Message}"
            );

        var message = queueResult.Value;
        AddRecipients(message, evt);

        await sentMessages.AddAsync(message, ct);
        await SaveOrThrowInProgressAsync(unitOfWork, evt.IdempotencyKey, ct);
        return message;
    }

    private static async Task<SentMessage> QueueAndPersistMailboxAsync(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ResolvedMailbox provider,
        ISentMessageRepository sentMessages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var queueResult = SentMessage.Queue(
            evt.TenantId,
            evt.IdempotencyKey,
            SensitiveNotificationContent.ForStorage(evt.TemplateKey, evt.Subject),
            provider.FromAddress,
            ParseEmailStream(evt.Stream),
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
        if (queueResult.IsFailure)
            throw new InvalidOperationException(
                $"Malformed {nameof(NotificationsEmailSendRequestedIntegrationEvent)}: {queueResult.Error.Message}"
            );

        var message = queueResult.Value;
        AddRecipients(message, evt);

        await sentMessages.AddAsync(message, ct);
        await SaveOrThrowInProgressAsync(unitOfWork, evt.IdempotencyKey, ct);
        return message;
    }

    /// <summary>
    /// Red de seguridad de defensa-en-profundidad (plan §Fase 11, punto 4): aun con el tri-state de
    /// <see cref="IIdempotencyGuard"/> corregido, dos reservas concurrentes podrían en teoría leer
    /// "no existe" antes de que cualquiera de las dos escriba — el índice único real de
    /// <c>SentMessages</c> (<c>SentMessageConfiguration</c>) es el backstop final para esa ventana
    /// angosta. Se trata igual que <see cref="IdempotencyReservationOutcome.InProgress"/>: nunca debe
    /// llegar como una excepción sin manejar hasta el middleware genérico.
    /// </summary>
    private static async Task SaveOrThrowInProgressAsync(
        IUnitOfWork unitOfWork,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConflictException)
        {
            throw new IdempotencyReservationInProgressException(idempotencyKey);
        }
    }

    private static void AddRecipients(SentMessage message, NotificationsEmailSendRequestedIntegrationEvent evt)
    {
        message.AddRecipient(evt.To, RecipientType.To, null);
        foreach (var cc in evt.Cc ?? [])
            message.AddRecipient(cc, RecipientType.Cc, null);
        foreach (var bcc in evt.Bcc ?? [])
            message.AddRecipient(bcc, RecipientType.Bcc, null);
    }

    /// <summary>
    /// Sin provider resuelto no hay <see cref="SentMessage"/> posible (ProviderCode/FromAddress son
    /// requeridos). La reserva de idempotencia queda sin completar a propósito — si el tenant
    /// configura su provider antes de que expire la ventana de retry, un reintento real del mismo
    /// evento puede todavía tener éxito.
    /// </summary>
    private static ValueTask PublishUnresolvedProviderCallbackAsync(
        IMessageBus bus,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ResolveResult resolveResult,
        ILogger logger,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        if (resolveResult.Status == ProviderResolutionStatus.ProviderNotConfigured)
        {
            return bus.PublishAsync(
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
        logger.LogWarning(
            "Could not resolve a provider for NotificationLog {NotificationLogId}: {Reason}",
            evt.NotificationLogId,
            reason
        );
        return bus.PublishAsync(
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

    /// <summary>
    /// A diferencia de <see cref="PublishUnresolvedProviderCallbackAsync"/> no hay caso
    /// SystemProviderMissing/ProviderUnhealthy: <see cref="MailboxResolutionStatus"/> solo distingue
    /// Resolved de ProviderNotConfigured (D3 §4.3) — la proyección local es la única fuente y no hay
    /// "salud" que evaluar ahí, la reserva de idempotencia también queda sin completar por el mismo
    /// motivo que en el camino SMTP.
    /// </summary>
    private static ValueTask PublishMailboxNotConfiguredCallbackAsync(
        IMessageBus bus,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        CancellationToken ct
    ) =>
        bus.PublishAsync(
            new PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                NotificationLogId = evt.NotificationLogId,
                DispatchAttemptId = evt.DispatchAttemptId,
                CampaignId = evt.CampaignId,
                EventAtUtc = DateTime.UtcNow,
            }
        );

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

    private static EmailStream ParseEmailStream(string value) =>
        Enum.TryParse<EmailStream>(value, ignoreCase: true, out var stream) ? stream : EmailStream.Transactional;

    internal static ProviderScope ParseProviderScope(string value) =>
        Enum.TryParse<ProviderScope>(value, ignoreCase: true, out var scope) ? scope : ProviderScope.System;

    internal static ProviderPriorityHint? ParsePriorityHint(string? value) =>
        Enum.TryParse<ProviderPriorityHint>(value, ignoreCase: true, out var hint) ? hint : null;

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
    internal static bool SystemFallbackAllowed(NotificationsEmailSendRequestedIntegrationEvent evt) =>
        !string.IsNullOrWhiteSpace(evt.ReplyTo) && ParseEmailStream(evt.Stream) == EmailStream.Transactional;

    /// <summary>
    /// Solo se anuncia a la oficina cuando el correo acabó saliendo por el sistema EN SU NOMBRE. En
    /// los otros escalones el From ya es el de la oficina y repetirlo sobraría.
    /// </summary>
    internal static string? FromDisplayNameFor(
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

    /// <summary>
    /// El Reply-To solo tiene sentido cuando el From no es ya el de la oficina: en los escalones de
    /// tenant, ponerlo apuntaría a la misma dirección que ya firma el correo.
    /// </summary>
    private static string? ReplyToFor(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ProviderScope effectiveScope
    ) => effectiveScope == ProviderScope.System ? evt.ReplyTo : null;
}
