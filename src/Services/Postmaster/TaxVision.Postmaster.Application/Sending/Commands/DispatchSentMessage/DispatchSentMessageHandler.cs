using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Consumers;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Application.Sending.Commands.MarkSendAttempted;
using TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;
using TaxVision.Postmaster.Application.Sending.Commands.ReleaseSendAttempt;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Postmaster.Application.Sending.Commands.DispatchSentMessage;

/// <summary>
/// Paso de envío (PMS-01), fuera de la transacción: graba el intento, llama al proveedor y registra el
/// resultado, cada uno en su propia transacción corta. Si el intento ya estaba grabado y no hay resultado,
/// el correo pudo haber salido: no se reenvía, se cierra como resultado desconocido.
/// </summary>
public static class DispatchSentMessageHandler
{
    /// <summary>Tope por adjunto en memoria (F6, el <c>.ics</c>): Notification ya aplica 256 KB al total.</summary>
    public const int MaxInlineAttachmentBytes = 256 * 1024;

    private static readonly TimeSpan DefaultRateLimitRetryAfter = TimeSpan.FromSeconds(60);

    /// <param name="envelope">La entrega actual (la inyecta Wolverine); null en llamadas directas.</param>
    [NonTransactional]
    public static async Task Handle(
        DispatchSentMessageCommand command,
        ISentMessageRepository sentMessages,
        IProviderResolver providerResolver,
        IEmailProviderRateLimiter rateLimiter,
        IEmailSender emailSender,
        IConnectedMailboxSender mailboxSender,
        IInlineAssetFetcher inlineAssetFetcher,
        IOutboundAttachmentFetcher attachmentFetcher,
        ITenantDirectoryRepository tenantDirectory,
        ICorrelationContext correlation,
        IMessageBus bus,
        RecordSendResultRetryOptions recordRetry,
        ILogger logger,
        CancellationToken ct,
        Envelope? envelope = null
    )
    {
        var evt = command.Request;
        using (correlation.Push(evt.CorrelationId))
        {
            var message = await sentMessages.GetForDispatchAsync(command.SentMessageId, ct);
            if (message is null || message.Status != SentMessageStatus.Sending)
                return; // ya resuelto: una entrega repetida no hace nada

            if (message.SendAttemptedAtUtc is { } attemptedAt)
            {
                logger.LogWarning(
                    "SentMessage {SentMessageId} reached the provider at {AttemptedAtUtc} without a recorded result; it is closed as unknown instead of being sent again.",
                    message.Id,
                    attemptedAt
                );
                const string unknown =
                    $"{SendOutcomeReasons.SendOutcomeUnknown}: the provider was called but the result was not recorded.";
                await RecordAsync(bus, recordRetry, command, Failed(unknown), unknown, fallback: null);
                return;
            }

            var inlineAssetRefs = ParseInlineAssetReferences(evt, logger);
            var content = new RenderedContent(evt.Subject, evt.HtmlBody, evt.TextBody, inlineAssetRefs);
            var inlineAssetBytes = await FetchInlineAssetBytesAsync(
                ResolveInlineAssetTenantId(evt),
                inlineAssetRefs,
                inlineAssetFetcher,
                logger,
                ct
            );
            // Antes de grabar el intento: si los adjuntos fallan, se sabe que nada salió.
            var attachments = await FetchAttachmentsAsync(evt, attachmentFetcher, logger, ct);
            if (attachments.IsFailure)
            {
                await RecordAsync(
                    bus,
                    recordRetry,
                    command,
                    Failed(attachments.Error.Message),
                    attachments.Error.Message,
                    null
                );
                return;
            }

            ResolvedEmailProvider? systemProvider = null;
            if (command.Lane == DeliveryLane.SystemProvider)
            {
                // Mismo pedido que el paso de preparación: las credenciales no viajan en el sobre.
                var resolved = await providerResolver.ResolveAsync(
                    evt.TenantId,
                    NotificationsEmailSendRequestedConsumer.ParseProviderScope(evt.RequiredProviderScope),
                    NotificationsEmailSendRequestedConsumer.ParsePriorityHint(evt.PriorityHint),
                    NotificationsEmailSendRequestedConsumer.SystemFallbackAllowed(evt),
                    ct
                );
                if (resolved.Status != ProviderResolutionStatus.Resolved)
                {
                    var reason = $"{resolved.Status}: {resolved.Reason}";
                    await RecordAsync(bus, recordRetry, command, Failed(reason), reason, null);
                    return;
                }
                systemProvider = resolved.Provider!;
            }

            var attempted = await SendAttempt.TryMarkAsync(bus, message.Id, ct);
            if (attempted.IsFailure)
            {
                logger.LogInformation(
                    "SentMessage {SentMessageId} is already being sent by another delivery: {Reason}",
                    message.Id,
                    attempted.Error.Message
                );
                return;
            }

            if (systemProvider is not null)
            {
                var sendResult = await emailSender.SendAsync(
                    message,
                    content,
                    systemProvider,
                    inlineAssetBytes,
                    attachments.Value,
                    ct
                );
                await RecordAsync(bus, recordRetry, command, sendResult, "Unknown SMTP failure.", null);
                return;
            }

            await SendThroughMailboxAsync(
                new MailboxSend(
                    command,
                    message,
                    content,
                    inlineAssetBytes,
                    attachments.Value,
                    Math.Max(1, envelope?.Attempts ?? 1)
                ),
                mailboxSender,
                new SystemLane(providerResolver, rateLimiter, emailSender),
                tenantDirectory,
                bus,
                recordRetry,
                logger,
                ct
            );
        }
    }

    private sealed record MailboxSend(
        DispatchSentMessageCommand Command,
        SentMessage Message,
        RenderedContent Content,
        IReadOnlyList<InlineAssetBytes> InlineAssets,
        IReadOnlyList<OutboundAttachmentBytes> Attachments,
        int Delivery
    );

    /// <summary>Lo que hace falta para salir por el proveedor del sistema desde la vía buzón.</summary>
    private sealed record SystemLane(
        IProviderResolver Resolver,
        IEmailProviderRateLimiter RateLimiter,
        IEmailSender Sender
    );

    /// <summary>
    /// Sin threading en v1 (D3 §11): el evento de Notification no trae identificadores nativos para responder
    /// un hilo. Los inline assets salen igual que por SMTP (<c>MailboxLaneParityTests</c>).
    /// </summary>
    private static async Task SendThroughMailboxAsync(
        MailboxSend send,
        IConnectedMailboxSender mailboxSender,
        SystemLane systemLane,
        ITenantDirectoryRepository tenantDirectory,
        IMessageBus bus,
        RecordSendResultRetryOptions recordRetry,
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

        MailboxFallback? fallback = null;
        if (ShouldFallBack(send.Message, send.Command.Request, sendResult))
        {
            var outcome = await FallBackToSystemAsync(
                send,
                sendResult.ErrorReason ?? "Office mailbox send failed.",
                systemLane,
                tenantDirectory,
                bus,
                logger,
                ct
            );
            fallback = outcome.Fallback;
            sendResult = outcome.Result ?? sendResult;
        }

        await RecordAsync(bus, recordRetry, send.Command, sendResult, "Unknown Connectors send failure.", fallback);
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
            NotificationsEmailSendRequestedConsumer.FromDisplayNameFor(onBehalfOf, system, ProviderScope.System),
            send.Command.Mailbox!.FromAddress,
            DateTime.UtcNow
        );
        if (fallback.ApplyTo(send.Message).IsFailure)
            return new FallbackOutcome(null, null);

        logger.LogInformation(
            "Office mailbox send failed for SentMessage {SentMessageId}; retrying through the system provider.",
            send.Message.Id
        );

        var quota = NotificationsEmailSendRequestedConsumer.ResolveStreamQuota(send.Message.Stream, system);
        if (quota is null)
            return new FallbackOutcome(
                fallback,
                Failed(
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
                Failed($"RateLimited: still over the provider quota after {send.Delivery} deliveries.")
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

    // Se registra aunque se esté apagando el host: el proveedor ya contestó.
    private static Task RecordAsync(
        IMessageBus bus,
        RecordSendResultRetryOptions retry,
        DispatchSentMessageCommand command,
        SendResult result,
        string fallbackFailureReason,
        MailboxFallback? fallback
    ) =>
        RecordSendResultRetry.InvokeAsync(
            bus,
            new RecordSendResultCommand(command.SentMessageId, result, fallbackFailureReason, fallback),
            retry
        );

    private static SendResult Failed(string reason) => new(false, null, reason, []);

    /// <summary>
    /// Los adjuntos fallan CERRADO, al revés que el logo: si no se pueden bajar, el correo no sale. Una
    /// factura que dice "A PDF copy is attached" y llega sin el PDF es un mensaje equivocado.
    /// </summary>
    private static async Task<Result<IReadOnlyList<OutboundAttachmentBytes>>> FetchAttachmentsAsync(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        IOutboundAttachmentFetcher attachmentFetcher,
        ILogger logger,
        CancellationToken ct
    )
    {
        var inline = DecodeInlineAttachments(evt, logger);
        if (evt.AttachmentFileIds is not { Count: > 0 } fileIds)
            return Result.Success<IReadOnlyList<OutboundAttachmentBytes>>(inline);

        var fetched = await attachmentFetcher.FetchByFileIdsAsync(evt.TenantId, fileIds, ct);
        if (fetched.IsSuccess)
            return Result.Success<IReadOnlyList<OutboundAttachmentBytes>>([.. fetched.Value, .. inline]);

        logger.LogWarning(
            "Failed to fetch {Count} attachment(s) for tenant {TenantId}: {Error}. The email will not be sent.",
            fileIds.Count,
            evt.TenantId,
            fetched.Error.Message
        );
        return Result.Failure<IReadOnlyList<OutboundAttachmentBytes>>(fetched.Error);
    }

    /// <summary>
    /// Adjuntos que viajan en el evento (base64) y no en CloudStorage. Uno inválido o demasiado grande se
    /// descarta con aviso y el correo sale igual: una invitación sin el <c>.ics</c> sigue siendo correcta.
    /// </summary>
    private static List<OutboundAttachmentBytes> DecodeInlineAttachments(
        NotificationsEmailSendRequestedIntegrationEvent evt,
        ILogger logger
    )
    {
        var result = new List<OutboundAttachmentBytes>();
        if (evt.InlineAttachments is not { Count: > 0 } attachments)
            return result;

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.FileName) || string.IsNullOrWhiteSpace(attachment.ContentType))
            {
                logger.LogWarning(
                    "Discarding inline attachment without file name or content type for tenant {TenantId}.",
                    evt.TenantId
                );
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(attachment.ContentBase64 ?? string.Empty);
            }
            catch (FormatException)
            {
                logger.LogWarning(
                    "Discarding inline attachment '{FileName}' for tenant {TenantId}: content is not valid base64.",
                    attachment.FileName,
                    evt.TenantId
                );
                continue;
            }

            if (bytes.Length == 0 || bytes.Length > MaxInlineAttachmentBytes)
            {
                logger.LogWarning(
                    "Discarding inline attachment '{FileName}' for tenant {TenantId}: {Bytes} bytes (max {Max}).",
                    attachment.FileName,
                    evt.TenantId,
                    bytes.Length,
                    MaxInlineAttachmentBytes
                );
                continue;
            }

            result.Add(new OutboundAttachmentBytes(attachment.FileName.Trim(), attachment.ContentType.Trim(), bytes));
        }

        return result;
    }

    /// <summary>
    /// Convierte las referencias del wire (<see cref="EmailInlineAssetReference"/>, sin validar) en el VO de
    /// dominio. Una referencia inválida se descarta con un warning: un logo roto no bloquea el envío.
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
    /// Descarga los bytes de CloudStorage para las referencias ya validadas. Degrada con gracia (log +
    /// <c>[]</c>): el peor caso es un ícono roto, no un mensaje perdido.
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

    /// <summary>
    /// El logo del sistema (<c>LogoScope == "System"</c>) vive en CloudStorage bajo <see cref="PlatformTenant"/>,
    /// no bajo el tenant del destinatario: con <c>evt.TenantId</c> el fetch devolvía 404.
    /// </summary>
    private static Guid ResolveInlineAssetTenantId(NotificationsEmailSendRequestedIntegrationEvent evt) =>
        string.Equals(evt.LogoScope, "System", StringComparison.OrdinalIgnoreCase) ? PlatformTenant.Id : evt.TenantId;
}
