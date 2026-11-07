using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Consume <c>connectors.raw_message_received.v1</c> (Connectors Fase 7/8). El correo de un cliente entra a su ficha;
/// el de quien todavía no es cliente entra sin cliente a la vista "Not a client" (D-COR-3) y se le enlaza cuando el
/// cliente aparece (<see cref="LinkUnlinkedMailHandler"/>). Fetch de body (Fase 5) y descarga de attachments
/// (Fase 8/12) están explícitamente fuera de alcance de este consumer.
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
        IIngestMetrics metrics,
        ICorrelationContext correlation,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var message = IncomingMessageNormalizer.Normalize(evt);
            var duplicate = await FindDuplicateAsync(evt, message, incomingEmails, ct);
            if (duplicate is not null)
            {
                logger.LogDebug(
                    "Duplicate message {InternetMessageId}/{ProviderMessageId} for tenant {TenantId}; skipping.",
                    message.InternetMessageId,
                    evt.ProviderMessageId,
                    evt.TenantId
                );
                // COR-03: el mismo mensaje en otro buzón es una copia, no un descarte. No avisa a su dueño.
                if (duplicate.AddMailboxCopy(evt.AccountId, evt.ProviderMessageId))
                    await unitOfWork.SaveChangesAsync(ct);
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
            // Los avisos de no entrega los procesa Connectors: un hilo "Not a client" los duplicaría.
            if (customer is null && options.Value.IsSystemSender(from.NormalizedValue))
            {
                logger.LogDebug("System sender for event {EventId} is not a customer; skipped.", evt.EventId);
                return;
            }

            // Transparencia: un correo que falla autenticación NO se pone en cuarentena oculta. Entra al
            // inbox marcado (SenderAuthentication → veredicto Unverified) para que el usuario lo vea y decida.
            await IngestMatchedMessageAsync(
                evt,
                message,
                customer?.CustomerId,
                from,
                incomingEmails,
                threads,
                threadResolver,
                unmatched,
                unitOfWork,
                metrics,
                logger,
                ct
            );
        }
    }

    // Sin InternetMessageId (o anulado por largo) se deduplica por el id del proveedor en ese buzón.
    private static Task<IncomingEmail?> FindDuplicateAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        NormalizedIncomingMessage message,
        IIncomingEmailRepository incomingEmails,
        CancellationToken ct
    ) =>
        message.InternetMessageId is null
            ? incomingEmails.FindByProviderMessageIdAsync(evt.TenantId, evt.AccountId, evt.ProviderMessageId, ct)
            : incomingEmails.FindByInternetMessageIdAsync(evt.TenantId, message.InternetMessageId, ct);

    private static SenderAuthentication BuildAuthentication(ConnectorsRawMessageReceivedIntegrationEvent evt) =>
        new(
            SenderAuthentication.Parse(evt.SpfResult),
            SenderAuthentication.Parse(evt.DkimResult),
            SenderAuthentication.Parse(evt.DmarcResult)
        );

    // El mensaje se acusa pero queda para reprocesarlo.
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
        Guid? customerId,
        EmailAddress from,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository threads,
        ThreadResolver threadResolver,
        IUnmatchedIncomingEmailRepository unmatched,
        IUnitOfWork unitOfWork,
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

        email.NotifyReceived(
            evt.MailboxOwnershipKnown ? evt.AccountId : null,
            evt.MailboxOwnershipKnown ? evt.MailboxOwnerUserId : null,
            evt.CorrelationId
        );
        // El hilo archivado volvió al Inbox: mismo aviso que el desarchivado manual.
        if (reopened)
            thread.NotifyStateChanged(correlationId: evt.CorrelationId);
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
