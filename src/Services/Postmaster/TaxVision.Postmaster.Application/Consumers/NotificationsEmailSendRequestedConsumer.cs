using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using Wolverine;

namespace TaxVision.Postmaster.Application.Consumers;

/// <summary>
/// Primer flujo end-to-end: recibe la solicitud de Notification, dedupa, resuelve provider, envía
/// y publica el callback correspondiente. Dedup adicional por <c>MessageId</c> vía
/// <c>UseDurableInbox</c> de Wolverine (ya activo a nivel de servicio, Fase 1).
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
/// <c>evt.InlineAssets</c>. Este consumer solo las valida (<see cref="ParseInlineAssetReferences"/>)
/// y descarga los bytes reales (<see cref="FetchInlineAssetBytesAsync"/>) — solo en el path SMTP
/// (<see cref="SendAndFinalizeAsync"/>); el path OAuth (<see cref="SendViaOAuthAndFinalizeAsync"/>)
/// sigue sin soporte, ver su propio comentario.
/// </item>
/// </list>
/// </remarks>
public static class NotificationsEmailSendRequestedConsumer
{
    public static async Task Handle(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        IIdempotencyGuard idempotencyGuard,
        IProviderResolver providerResolver,
        IOAuthProviderResolver oauthProviderResolver,
        ISuppressionListRepository suppressionList,
        IEmailProviderRateLimiter rateLimiter,
        IEmailSender emailSender,
        IOAuthEmailSender oauthEmailSender,
        IInlineAssetFetcher inlineAssetFetcher,
        ISentMessageRepository sentMessages,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
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

            if (ParseProviderScope(evt.RequiredProviderScope) == ProviderScope.TenantOAuth)
            {
                await HandleTenantOAuthPathAsync(
                    evt,
                    oauthProviderResolver,
                    suppressionList,
                    oauthEmailSender,
                    sentMessages,
                    idempotencyGuard,
                    unitOfWork,
                    bus,
                    ct
                );
                return;
            }

            await HandleSmtpPathAsync(
                evt,
                providerResolver,
                suppressionList,
                rateLimiter,
                emailSender,
                inlineAssetFetcher,
                sentMessages,
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
        IProviderResolver providerResolver,
        ISuppressionListRepository suppressionList,
        IEmailProviderRateLimiter rateLimiter,
        IEmailSender emailSender,
        IInlineAssetFetcher inlineAssetFetcher,
        ISentMessageRepository sentMessages,
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
            ct
        );
        if (resolveResult.Status != ProviderResolutionStatus.Resolved)
        {
            await PublishUnresolvedProviderCallbackAsync(bus, evt, resolveResult, logger, ct);
            return;
        }

        var message = await QueueAndPersistAsync(evt, resolveResult.Provider!, sentMessages, unitOfWork, ct);
        if (await ApplySuppressionAsync(message, evt, suppressionList, idempotencyGuard, unitOfWork, bus, ct))
            return;

        if (
            await ApplyRateLimitAsync(
                message,
                evt,
                resolveResult.Provider!,
                rateLimiter,
                idempotencyGuard,
                unitOfWork,
                bus,
                ct
            )
        )
            return;

        await SendAndFinalizeAsync(
            message,
            evt,
            resolveResult.Provider!,
            emailSender,
            inlineAssetFetcher,
            idempotencyGuard,
            unitOfWork,
            bus,
            logger,
            ct
        );
    }

    /// <summary>
    /// Sin control de cupo propio a diferencia de <see cref="HandleSmtpPathAsync"/>: el rate limit del
    /// canal OAuth ya lo aplica Connectors por (tenant, cuenta) en su M2M de envío (D3 §4.4/§7,
    /// <c>ISendRateLimiter</c>, default 20/min) — duplicarlo acá sería el mismo cupo enforced dos veces
    /// con configuraciones potencialmente divergentes.
    /// </summary>
    private static async Task HandleTenantOAuthPathAsync(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        IOAuthProviderResolver oauthProviderResolver,
        ISuppressionListRepository suppressionList,
        IOAuthEmailSender oauthEmailSender,
        ISentMessageRepository sentMessages,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var resolveResult = await oauthProviderResolver.ResolveAsync(evt.TenantId, ct);
        if (resolveResult.Status != OAuthResolutionStatus.Resolved)
        {
            await PublishOAuthProviderNotConfiguredCallbackAsync(bus, evt, ct);
            return;
        }

        var message = await QueueAndPersistOAuthAsync(evt, resolveResult.Provider!, sentMessages, unitOfWork, ct);
        if (await ApplySuppressionAsync(message, evt, suppressionList, idempotencyGuard, unitOfWork, bus, ct))
            return;

        await SendViaOAuthAndFinalizeAsync(
            message,
            evt,
            resolveResult.Provider!,
            oauthEmailSender,
            idempotencyGuard,
            unitOfWork,
            bus,
            ct
        );
    }

    /// <summary>
    /// Marca como Suppressed los recipients cuya dirección está en la lista negra del tenant. Si TODOS
    /// quedan suprimidos, el mensaje entero pasa a <see cref="SentMessageStatus.Suppressed"/> sin
    /// intentar el envío y se publica el callback correspondiente (devuelve true — el caller no debe
    /// seguir). Si solo ALGUNOS quedan suprimidos, el envío continúa para el resto — <c>MimeMessageBuilder</c>
    /// excluye los recipients Suppressed del MIME real (Fase 7).
    /// </summary>
    private static async Task<bool> ApplySuppressionAsync(
        SentMessage message,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ISuppressionListRepository suppressionList,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var addresses = message.Recipients.Select(r => r.Address).Distinct().ToList();
        var suppressed = await suppressionList.GetSuppressedAsync(evt.TenantId, addresses, ct);
        if (suppressed.Count == 0)
            return false;

        var now = DateTime.UtcNow;
        foreach (var recipient in message.Recipients.Where(r => suppressed.Contains(r.Address)))
            message.RecordDeliveryEvent(
                recipient.Id,
                SentMessageEventType.Suppressed,
                now,
                rawPayload: null,
                "Address in suppression list."
            );

        if (!message.Recipients.All(r => r.Status == RecipientStatus.Suppressed))
        {
            await unitOfWork.SaveChangesAsync(ct);
            return false;
        }

        message.MarkAsSuppressed("All recipients are in the suppression list.", now);
        await idempotencyGuard.CompleteAsync(evt.TenantId, evt.IdempotencyKey, message.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new PostmasterEmailDeliverySuppressedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                NotificationLogId = evt.NotificationLogId,
                DispatchAttemptId = evt.DispatchAttemptId,
                SentMessageId = message.Id,
                SuppressionReason = "All recipients are in the suppression list.",
                EventAtUtc = now,
            }
        );
        return true;
    }

    /// <summary>
    /// Cupo por (ProviderCode, TenantId) — <c>provider.RateLimitPerMinute</c> ya viene resuelto (Fase 3/3.5).
    /// Si se agota, el mensaje va directo a Failed (terminal, sin reintento automático de Postmaster —
    /// el reintento real depende de que Notification vuelva a publicar el evento).
    /// </summary>
    private static async Task<bool> ApplyRateLimitAsync(
        SentMessage message,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ResolvedEmailProvider provider,
        IEmailProviderRateLimiter rateLimiter,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var decision = await rateLimiter.AcquireAsync(
            provider.ProviderCode,
            evt.TenantId,
            provider.RateLimitPerMinute,
            ct
        );
        if (decision.Allowed)
            return false;

        var now = DateTime.UtcNow;
        var reason =
            $"RateLimited: retry after {(int)(decision.RetryAfter ?? TimeSpan.FromSeconds(60)).TotalSeconds}s.";
        message.MarkAsFailed(reason, now);
        await idempotencyGuard.CompleteAsync(evt.TenantId, evt.IdempotencyKey, message.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await PublishFailedAsync(bus, evt, message.Id, providerMessageId: null, reason, now, ct);
        return true;
    }

    private static async Task<SentMessage> QueueAndPersistAsync(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ResolvedEmailProvider provider,
        ISentMessageRepository sentMessages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var queueResult = SentMessage.Queue(
            evt.TenantId,
            evt.IdempotencyKey,
            evt.Subject,
            provider.FromAddress,
            ParseEmailStream(evt.Stream),
            provider.ProviderCode,
            evt.NotificationLogId,
            evt.CorrelationId,
            provider.FromDisplayName,
            replyTo: null,
            evt.TemplateKey,
            DateTime.UtcNow,
            ParseProviderScope(evt.RequiredProviderScope)
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

    private static async Task<SentMessage> QueueAndPersistOAuthAsync(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ResolvedOAuthProvider provider,
        ISentMessageRepository sentMessages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var queueResult = SentMessage.Queue(
            evt.TenantId,
            evt.IdempotencyKey,
            evt.Subject,
            provider.FromAddress,
            ParseEmailStream(evt.Stream),
            provider.ProviderCode,
            evt.NotificationLogId,
            evt.CorrelationId,
            provider.FromDisplayName,
            replyTo: null,
            evt.TemplateKey,
            DateTime.UtcNow,
            ProviderScope.TenantOAuth
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
                EventAtUtc = now,
            }
        );
    }

    /// <summary>
    /// A diferencia de <see cref="PublishUnresolvedProviderCallbackAsync"/> no hay caso
    /// SystemProviderMissing/ProviderUnhealthy: <see cref="OAuthResolutionStatus"/> solo distingue
    /// Resolved de ProviderNotConfigured (D3 §4.3) — la proyección local es la única fuente y no hay
    /// "salud" que evaluar ahí, la reserva de idempotencia también queda sin completar por el mismo
    /// motivo que en el camino SMTP.
    /// </summary>
    private static ValueTask PublishOAuthProviderNotConfiguredCallbackAsync(
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
                EventAtUtc = DateTime.UtcNow,
            }
        );

    /// <summary>
    /// Sin threading en v1 (D3 §11, pendiente documentado): el evento de Notification no trae
    /// identificadores nativos del proveedor para responder un hilo existente, así que los 3
    /// parámetros de threading de <see cref="IOAuthEmailSender.SendAsync"/> van null — mismo criterio
    /// de incrementalidad que Postmaster ya usó para inline assets en Fase 3.5.
    ///
    /// Hardening Fase 9: a diferencia de <see cref="SendAndFinalizeAsync"/> (path SMTP), acá NO se
    /// resuelven <c>evt.InlineAssets</c> — <see cref="IOAuthEmailSender.SendAsync"/> no tiene ningún
    /// parámetro de inline assets (Connectors envía vía Gmail/Graph API, no arma el MIME multipart/
    /// related que <c>MimeMessageBuilder</c> construye para SMTP). Agregar soporte de logo CID al path
    /// OAuth es una pieza de plomería genuinamente nueva (extender el contrato de
    /// <see cref="IOAuthEmailSender"/> y el M2M de envío de Connectors) — fuera del alcance declarado
    /// de esta fase ("conectar lo ya construido", no construir una capacidad nueva).
    /// </summary>
    private static async Task SendViaOAuthAndFinalizeAsync(
        SentMessage message,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ResolvedOAuthProvider provider,
        IOAuthEmailSender oauthEmailSender,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        message.MarkAsSending();
        var content = new RenderedContent(evt.Subject, evt.HtmlBody, evt.TextBody);
        var sendResult = await oauthEmailSender.SendAsync(
            message,
            content,
            provider,
            inReplyToInternetMessageId: null,
            references: null,
            replyToProviderMessageId: null,
            attachments: [],
            ct
        );

        var now = DateTime.UtcNow;
        if (sendResult.Success)
            message.MarkAsSent(sendResult.ProviderMessageId, now);
        else
            message.MarkAsFailed(sendResult.ErrorReason ?? "Unknown Connectors send failure.", now);

        await idempotencyGuard.CompleteAsync(evt.TenantId, evt.IdempotencyKey, message.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);

        if (sendResult.Success)
            await PublishSucceededAsync(bus, evt, message.Id, sendResult.ProviderMessageId, ct);
        else
            await PublishFailedAsync(
                bus,
                evt,
                message.Id,
                sendResult.ProviderMessageId,
                sendResult.ErrorReason ?? "Unknown Connectors send failure.",
                now,
                ct
            );
    }

    /// <summary>
    /// Hardening Fase 9: antes, esto siempre pasaba <c>inlineAssets: []</c> a <see cref="IEmailSender"/>
    /// — el pipeline de logos CID (Scribe Fase 4.5) estaba completo en cada punta pero nunca conectado
    /// entre ellas, así que ningún logo llegaba a un email real. Ahora resuelve las referencias que
    /// trae <paramref name="evt"/> vía <see cref="IInlineAssetFetcher"/> (registrado en DI desde Fase
    /// 3.5, nunca invocado hasta esta fase) antes de armar el MIME.
    /// </summary>
    private static async Task SendAndFinalizeAsync(
        SentMessage message,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ResolvedEmailProvider provider,
        IEmailSender emailSender,
        IInlineAssetFetcher inlineAssetFetcher,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        message.MarkAsSending();
        var inlineAssetRefs = ParseInlineAssetReferences(evt, logger);
        var content = new RenderedContent(evt.Subject, evt.HtmlBody, evt.TextBody, inlineAssetRefs);
        var inlineAssetBytes = await FetchInlineAssetBytesAsync(
            ResolveInlineAssetTenantId(evt),
            inlineAssetRefs,
            inlineAssetFetcher,
            logger,
            ct
        );
        var sendResult = await emailSender.SendAsync(message, content, provider, inlineAssetBytes, ct);

        var now = DateTime.UtcNow;
        if (sendResult.Success)
            message.MarkAsSent(sendResult.ProviderMessageId, now);
        else
            message.MarkAsFailed(sendResult.ErrorReason ?? "Unknown SMTP failure.", now);

        await idempotencyGuard.CompleteAsync(evt.TenantId, evt.IdempotencyKey, message.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);

        if (sendResult.Success)
            await PublishSucceededAsync(bus, evt, message.Id, sendResult.ProviderMessageId, ct);
        else
            await PublishFailedAsync(
                bus,
                evt,
                message.Id,
                sendResult.ProviderMessageId,
                sendResult.ErrorReason ?? "Unknown SMTP failure.",
                now,
                ct
            );
    }

    /// <summary>
    /// Convierte las referencias del wire (<see cref="EmailInlineAssetReference"/>, sin validar) en el
    /// VO real de dominio (<see cref="InlineAsset"/>, que sí valida tamaño/shape en su factory). Una
    /// referencia individualmente inválida se descarta con un warning en vez de tirar abajo el envío
    /// completo — un logo roto no debería bloquear un email transaccional real.
    /// </summary>
    private static IReadOnlyList<InlineAsset> ParseInlineAssetReferences(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ILogger logger
    )
    {
        if (evt.InlineAssets is not { Count: > 0 })
            return [];

        var parsed = new List<InlineAsset>(evt.InlineAssets.Count);
        foreach (var reference in evt.InlineAssets)
        {
            var result = InlineAsset.Create(
                reference.ContentId,
                reference.CloudStorageFileId,
                reference.ContentType,
                reference.SizeBytes
            );
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Discarding invalid inline asset reference '{ContentId}' for tenant {TenantId}: {Error}",
                    reference.ContentId,
                    evt.TenantId,
                    result.Error.Message
                );
                continue;
            }

            parsed.Add(result.Value);
        }

        return parsed;
    }

    /// <summary>
    /// Descarga los bytes reales de CloudStorage para las referencias ya validadas. Degrada con
    /// gracia (log + <c>[]</c>) si el fetch falla en vez de fallar el envío entero — un logo que no
    /// se pudo descargar no debería bloquear un email transaccional (password reset, invitación,
    /// etc.); el peor caso es un ícono roto en el cliente de correo, no un mensaje perdido.
    /// </summary>
    private static async Task<IReadOnlyList<InlineAssetBytes>> FetchInlineAssetBytesAsync(
        Guid tenantId,
        IReadOnlyList<InlineAsset> inlineAssets,
        IInlineAssetFetcher inlineAssetFetcher,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (inlineAssets.Count == 0)
            return [];

        var fetched = await inlineAssetFetcher.FetchAllAsync(tenantId, inlineAssets, ct);
        if (fetched.IsSuccess)
            return fetched.Value;

        logger.LogWarning(
            "Failed to fetch {Count} inline asset(s) for tenant {TenantId}: {Error}. Sending without inline logo.",
            inlineAssets.Count,
            tenantId,
            fetched.Error.Message
        );
        return [];
    }

    private static ValueTask PublishSucceededAsync(
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
                EventAtUtc = DateTime.UtcNow,
            }
        );

    private static ValueTask PublishFailedAsync(
        IMessageBus bus,
        NotificationsEmailSendRequestedIntegrationEvent evt,
        Guid sentMessageId,
        string? providerMessageId,
        string reason,
        DateTime eventAtUtc,
        CancellationToken ct
    ) =>
        bus.PublishAsync(
            new PostmasterEmailDeliveryFailedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                NotificationLogId = evt.NotificationLogId,
                DispatchAttemptId = evt.DispatchAttemptId,
                SentMessageId = sentMessageId,
                ProviderMessageId = providerMessageId,
                Reason = reason,
                EventAtUtc = eventAtUtc,
            }
        );

    private static EmailStream ParseEmailStream(string value) =>
        Enum.TryParse<EmailStream>(value, ignoreCase: true, out var stream) ? stream : EmailStream.Transactional;

    private static ProviderScope ParseProviderScope(string value) =>
        Enum.TryParse<ProviderScope>(value, ignoreCase: true, out var scope) ? scope : ProviderScope.System;

    private static ProviderPriorityHint? ParsePriorityHint(string? value) =>
        Enum.TryParse<ProviderPriorityHint>(value, ignoreCase: true, out var hint) ? hint : null;

    /// <summary>
    /// Tenant a usar para el fetch M2M de los inline assets en CloudStorage — NO siempre es
    /// <c>evt.TenantId</c>. El logo del sistema (<c>evt.LogoScope == "System"</c>, Scribe Fase 4.5)
    /// vive en CloudStorage bajo <see cref="PlatformTenant"/>, no bajo el tenant del destinatario del
    /// email; <c>ICloudStorageInlineAssetFetcher</c> pide el token M2M y CloudStorage filtra
    /// <c>Files</c> por tenant (<c>FilesController.IssueDownloadUrl</c>), así que usar
    /// <c>evt.TenantId</c> ahí siempre devolvía 404 para el logo del sistema. <c>evt.LogoScope</c> ya
    /// existía en el contrato para esto exacto (ver <see cref="NotificationsEmailSendRequestedIntegrationEvent.LogoScope"/>)
    /// pero nunca se leía en este consumer.
    /// </summary>
    private static Guid ResolveInlineAssetTenantId(NotificationsEmailSendRequestedIntegrationEvent evt) =>
        string.Equals(evt.LogoScope, "System", StringComparison.OrdinalIgnoreCase) ? PlatformTenant.Id : evt.TenantId;
}
