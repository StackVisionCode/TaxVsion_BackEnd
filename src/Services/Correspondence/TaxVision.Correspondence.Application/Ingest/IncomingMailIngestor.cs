using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>Deja el correo en su hilo: deduplica, resuelve el hilo, arma el correo y guarda.</summary>
public sealed class IncomingMailIngestor(
    IIncomingEmailRepository incomingEmails,
    IEmailThreadRepository threads,
    ThreadResolver threadResolver,
    IngestRejectionRecorder rejections,
    IUnitOfWork unitOfWork,
    ILogger<IncomingMailIngestor> logger
)
{
    /// <summary>True si el mensaje ya estaba y no hay nada más que hacer.</summary>
    public async Task<bool> TryRecordDuplicateAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        NormalizedIncomingMessage message,
        CancellationToken ct
    )
    {
        var duplicate = await FindDuplicateAsync(evt, message, ct);
        if (duplicate is null)
            return false;

        logger.LogDebug(
            "Duplicate message {InternetMessageId}/{ProviderMessageId} for tenant {TenantId}; skipping.",
            message.InternetMessageId,
            evt.ProviderMessageId,
            evt.TenantId
        );
        // COR-03: el mismo mensaje en otro buzón es una copia, no un descarte. No avisa a su dueño.
        if (duplicate.AddMailboxCopy(evt.AccountId, evt.ProviderMessageId))
            await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    // Sin InternetMessageId (o anulado por largo) se deduplica por el id del proveedor en ese buzón.
    private Task<IncomingEmail?> FindDuplicateAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        NormalizedIncomingMessage message,
        CancellationToken ct
    ) =>
        message.InternetMessageId is null
            ? incomingEmails.FindByProviderMessageIdAsync(evt.TenantId, evt.AccountId, evt.ProviderMessageId, ct)
            : incomingEmails.FindByInternetMessageIdAsync(evt.TenantId, message.InternetMessageId, ct);

    public async Task IngestAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        NormalizedIncomingMessage message,
        Guid? customerId,
        EmailAddress from,
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

        var attached = EmailThreadIngestion.Attach(
            resolution.MatchedThread,
            resolution.UnthreadedSentDraft,
            new InboundThreadMessage(
                evt.TenantId,
                customerId,
                message.Subject,
                message.ProviderThreadId,
                evt.ReceivedAtUtc
            )
        );
        if (attached.IsFailure)
        {
            await rejections.RejectIngestAsync(evt, message, attached.Error, ct);
            return;
        }
        var attachment = attached.Value;
        var thread = attachment.Thread;

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
            await rejections.RejectIngestAsync(evt, message, emailResult.Error, ct);
            return;
        }
        var email = emailResult.Value;

        // El hilo se toca recién con el correo válido: un rechazo no deja hilos vacíos ni contadores inflados.
        var committed = attachment.Commit();
        if (committed.IsFailure)
        {
            await rejections.RejectIngestAsync(evt, message, committed.Error, ct);
            return;
        }
        if (attachment.IsNewThread)
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
        if (committed.Value)
            thread.NotifyStateChanged(correlationId: evt.CorrelationId);
    }

    private static SenderAuthentication BuildAuthentication(ConnectorsRawMessageReceivedIntegrationEvent evt) =>
        new(
            SenderAuthentication.Parse(evt.SpfResult),
            SenderAuthentication.Parse(evt.DkimResult),
            SenderAuthentication.Parse(evt.DmarcResult)
        );

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
