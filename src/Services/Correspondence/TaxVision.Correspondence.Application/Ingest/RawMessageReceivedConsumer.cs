using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Consume <c>connectors.raw_message_received.v1</c> (Connectors Fase 7/8) y decide si el
/// mensaje entra al inbox de un customer conocido del tenant. Flujo completo en
/// Correspondence_Service_Design_And_Implementation_Plan.md §36 Fase 4; <see cref="UnmatchedIncomingEmail"/>
/// en §14. Fetch de body (Fase 5) y descarga de attachments (Fase 8/12) están explícitamente
/// fuera de alcance de este consumer.
/// </summary>
public static class RawMessageReceivedConsumer
{
    public static async Task Handle(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository threads,
        ThreadResolver threadResolver,
        ICustomerEmailAddressRepository customerEmails,
        IUnmatchedIncomingEmailRepository unmatched,
        IOptions<CorrespondenceIngestOptions> options,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IIngestMetrics metrics,
        ICorrelationContext correlation,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var message = IncomingMessageNormalizer.Normalize(evt);
            if (await IsDuplicateAsync(evt, message, incomingEmails, ct))
            {
                logger.LogDebug(
                    "Duplicate message {InternetMessageId}/{ProviderMessageId} for tenant {TenantId}; skipping.",
                    message.InternetMessageId,
                    evt.ProviderMessageId,
                    evt.TenantId
                );
                return;
            }

            var fromResult = EmailAddress.Create(evt.From);
            if (fromResult.IsFailure)
            {
                logger.LogWarning(
                    "raw_message_received event {EventId} has an invalid From address; recorded as InvalidSender.",
                    evt.EventId
                );
                await RecordRejectionAsync(
                    evt,
                    message,
                    UnmatchedReason.InvalidSender,
                    fromResult.Error.Code,
                    unmatched,
                    unitOfWork,
                    logger,
                    ct
                );
                return;
            }
            var from = fromResult.Value;

            var customer = await customerEmails.FindActiveByAddressAsync(evt.TenantId, from.NormalizedValue, ct);
            if (customer is null)
            {
                await HandleUnmatchedSenderAsync(evt, from, options.Value, unmatched, unitOfWork, logger, ct);
                return;
            }

            // Transparencia: un correo que falla autenticación NO se pone en cuarentena oculta. Entra al
            // inbox marcado (SenderAuthentication → veredicto Unverified) para que el usuario lo vea y decida.
            await IngestMatchedMessageAsync(
                evt,
                message,
                customer.CustomerId,
                from,
                incomingEmails,
                threads,
                threadResolver,
                unmatched,
                unitOfWork,
                bus,
                metrics,
                logger,
                ct
            );
        }
    }

    private static async Task<bool> IsDuplicateAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        NormalizedIncomingMessage message,
        IIncomingEmailRepository incomingEmails,
        CancellationToken ct
    )
    {
        // Sin InternetMessageId (o anulado por largo) se deduplica por el id del proveedor en ese buzón.
        var existing = message.InternetMessageId is null
            ? await incomingEmails.FindByProviderMessageIdAsync(evt.TenantId, evt.AccountId, evt.ProviderMessageId, ct)
            : await incomingEmails.FindByInternetMessageIdAsync(evt.TenantId, message.InternetMessageId, ct);
        return existing is not null;
    }

    private static async Task HandleUnmatchedSenderAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        EmailAddress from,
        CorrespondenceIngestOptions options,
        IUnmatchedIncomingEmailRepository unmatched,
        IUnitOfWork unitOfWork,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        if (!options.EnableUnmatchedDebug)
            return;

        await RecordUnmatchedAsync(evt, from, UnmatchedReason.NoCustomerMatch, unmatched, logger, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static SenderAuthentication BuildAuthentication(ConnectorsRawMessageReceivedIntegrationEvent evt) =>
        new(
            SenderAuthentication.Parse(evt.SpfResult),
            SenderAuthentication.Parse(evt.DkimResult),
            SenderAuthentication.Parse(evt.DmarcResult)
        );

    private static async Task RecordUnmatchedAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        EmailAddress from,
        UnmatchedReason reason,
        IUnmatchedIncomingEmailRepository unmatched,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        var result = UnmatchedIncomingEmail.Create(
            evt.TenantId,
            from,
            evt.Subject,
            evt.ProviderMessageId,
            evt.ReceivedAtUtc,
            reason
        );
        if (result.IsFailure)
        {
            logger.LogWarning(
                "UnmatchedIncomingEmail.Create failed for event {EventId}: {Error}",
                evt.EventId,
                result.Error.Code
            );
            return;
        }

        await unmatched.AddAsync(result.Value, ct);
    }

    // Siempre se guarda (no depende de EnableUnmatchedDebug): el mensaje se acusa pero queda para reprocesarlo.
    private static async Task RecordRejectionAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        NormalizedIncomingMessage message,
        UnmatchedReason reason,
        string rejectionCode,
        IUnmatchedIncomingEmailRepository unmatched,
        IUnitOfWork unitOfWork,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        var result = UnmatchedIncomingEmail.CreateRejected(
            evt.TenantId,
            evt.From,
            message.Subject,
            evt.ProviderMessageId,
            evt.ReceivedAtUtc,
            reason,
            rejectionCode,
            evt.AccountId,
            evt.ProviderCode,
            evt.InternetMessageId
        );
        if (result.IsFailure)
        {
            logger.LogError(
                "Could not record rejected event {EventId} ({Reason}, {RejectionCode}): {Error}",
                evt.EventId,
                reason,
                rejectionCode,
                result.Error.Code
            );
            return;
        }

        await unmatched.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static async Task RejectIngestAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        NormalizedIncomingMessage message,
        string rejectionCode,
        IUnmatchedIncomingEmailRepository unmatched,
        IUnitOfWork unitOfWork,
        IIngestMetrics metrics,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        logger.LogError(
            "Ingest rejected event {EventId} for tenant {TenantId}: {RejectionCode}; recorded as IngestRejected.",
            evt.EventId,
            evt.TenantId,
            rejectionCode
        );
        metrics.IngestRejected(rejectionCode);
        await RecordRejectionAsync(
            evt,
            message,
            UnmatchedReason.IngestRejected,
            rejectionCode,
            unmatched,
            unitOfWork,
            logger,
            ct
        );
    }

    private static async Task IngestMatchedMessageAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        NormalizedIncomingMessage message,
        Guid customerId,
        EmailAddress from,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository threads,
        ThreadResolver threadResolver,
        IUnmatchedIncomingEmailRepository unmatched,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IIngestMetrics metrics,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        var resolution = await threadResolver.ResolveAsync(
            evt.TenantId,
            customerId,
            message.ProviderThreadId,
            message.InReplyTo,
            message.References,
            message.Subject,
            evt.ReceivedAtUtc,
            ct
        );

        var thread = resolution.MatchedThread;
        var sentDraft = resolution.UnthreadedSentDraft;
        if (thread is null && sentDraft is not null)
        {
            // COR-16: el correo nuevo del CRM recibe su hilo con la primera respuesta (mismo id que usa el aviso de
            // no entrega, así los dos caminos no crean dos hilos).
            var sentThreadResult = EmailThread.NewForSentEmail(
                sentDraft.Id,
                evt.TenantId,
                customerId,
                sentDraft.Subject,
                sentDraft.UpdatedAtUtc
            );
            if (sentThreadResult.IsFailure)
            {
                await RejectIngestAsync(
                    evt,
                    message,
                    sentThreadResult.Error.Code,
                    unmatched,
                    unitOfWork,
                    metrics,
                    logger,
                    ct
                );
                return;
            }
            thread = sentThreadResult.Value;
        }
        else if (thread is null)
        {
            var createResult = EmailThread.NewFromMessage(
                evt.TenantId,
                customerId,
                message.Subject,
                message.ProviderThreadId,
                evt.ReceivedAtUtc
            );
            if (createResult.IsFailure)
            {
                await RejectIngestAsync(
                    evt,
                    message,
                    createResult.Error.Code,
                    unmatched,
                    unitOfWork,
                    metrics,
                    logger,
                    ct
                );
                return;
            }
            thread = createResult.Value;
        }

        var emailResult = IncomingEmail.Create(
            evt.TenantId,
            customerId,
            thread.Id,
            evt.AccountId,
            evt.ProviderCode,
            evt.ProviderMessageId,
            from,
            fromDisplayName: NormalizeDisplayName(evt.FromDisplayName),
            message.Subject,
            message.Snippet,
            evt.ReceivedAtUtc,
            evt.HasAttachments,
            evt.AttachmentCount,
            message.InternetMessageId,
            message.InReplyTo,
            JoinReferences(message.References),
            BuildRecipients(evt),
            BuildAttachments(evt),
            BuildAuthentication(evt)
        );
        if (emailResult.IsFailure)
        {
            await RejectIngestAsync(evt, message, emailResult.Error.Code, unmatched, unitOfWork, metrics, logger, ct);
            return;
        }
        var email = emailResult.Value;

        // El hilo se toca recién con el correo válido: un rechazo no deja hilos vacíos ni contadores inflados.
        if (sentDraft is not null)
        {
            var linkResult = sentDraft.LinkToThread(thread.Id);
            if (linkResult.IsFailure)
            {
                await RejectIngestAsync(
                    evt,
                    message,
                    linkResult.Error.Code,
                    unmatched,
                    unitOfWork,
                    metrics,
                    logger,
                    ct
                );
                return;
            }
        }

        var reopened = false;
        if (resolution.MatchedThread is not null || sentDraft is not null)
        {
            var appendResult = thread.AppendMessage(evt.ReceivedAtUtc);
            if (appendResult.IsFailure)
            {
                await RejectIngestAsync(
                    evt,
                    message,
                    appendResult.Error.Code,
                    unmatched,
                    unitOfWork,
                    metrics,
                    logger,
                    ct
                );
                return;
            }
            reopened = appendResult.Value;
            // COR-16: el hilo de un enviado nace sin id del proveedor; se completa con el de la respuesta.
            thread.AdoptProviderThreadId(message.ProviderThreadId);
        }
        if (resolution.MatchedThread is null)
            await threads.AddAsync(thread, ct);

        await incomingEmails.AddAsync(email, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Re-sincronización (CON-07): correo viejo recuperado, sin campana ni aviso de hilo reabierto.
        if (evt.IsReplay)
            return;

        await bus.PublishAsync(
            new CorrespondenceCustomerEmailReceivedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                CustomerId = customerId,
                IncomingEmailId = email.Id,
                EmailThreadId = thread.Id,
                Subject = message.Subject,
                AccountId = evt.MailboxOwnershipKnown ? evt.AccountId : null,
                MailboxOwnerUserId = evt.MailboxOwnershipKnown ? evt.MailboxOwnerUserId : null,
            }
        );

        // El hilo archivado volvió al Inbox: mismo aviso que el desarchivado manual.
        if (reopened)
            await ThreadStateNotifier.PublishAsync(bus, incomingEmails, thread, null, evt.CorrelationId, ct);
    }

    private static string? JoinReferences(IReadOnlyList<string>? references) =>
        references is null || references.Count == 0 ? null : string.Join(",", references);

    // Trim + null si viene vacío + tope a FromDisplayNameMaxLength (200) para no romper la validación
    // del aggregate con un nombre exótico demasiado largo.
    private static string? NormalizeDisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim();
        return trimmed.Length > IncomingEmail.FromDisplayNameMaxLength
            ? trimmed[..IncomingEmail.FromDisplayNameMaxLength]
            : trimmed;
    }

    private static List<IncomingEmailRecipientData> BuildRecipients(ConnectorsRawMessageReceivedIntegrationEvent evt)
    {
        var recipients = new List<IncomingEmailRecipientData>();
        AddRecipients(recipients, evt.To, EmailRecipientType.To);
        AddRecipients(recipients, evt.Cc, EmailRecipientType.Cc);
        AddRecipients(recipients, evt.Bcc, EmailRecipientType.Bcc);
        return recipients;
    }

    private static void AddRecipients(
        List<IncomingEmailRecipientData> target,
        IReadOnlyList<string> addresses,
        EmailRecipientType type
    )
    {
        foreach (var raw in addresses)
        {
            var result = EmailAddress.Create(raw);
            if (result.IsSuccess)
                target.Add(new IncomingEmailRecipientData(result.Value, type, null));
        }
    }

    private static List<IncomingEmailAttachmentData> BuildAttachments(ConnectorsRawMessageReceivedIntegrationEvent evt)
    {
        var attachments = new List<IncomingEmailAttachmentData>();
        foreach (var meta in evt.AttachmentMetadata ?? [])
            attachments.Add(
                new IncomingEmailAttachmentData(
                    meta.Filename,
                    meta.ContentType,
                    meta.SizeBytes,
                    meta.ProviderAttachmentId,
                    false,
                    meta.PartId
                )
            );

        return attachments;
    }
}
