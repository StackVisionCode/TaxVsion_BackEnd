using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using Wolverine;

namespace TaxVision.Correspondence.Application.Files;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen. Correspondence declara los adjuntos suyos de esa oficina
/// (enviados y entrantes ya guardados) con los mismos valores que la reposición manual; los ids ajenos se ignoran.
/// </summary>
public static class FileSourcesClaimConsumer
{
    public static async Task Handle(
        FileSourcesClaimRequestedIntegrationEvent evt,
        IDraftRepository drafts,
        IIncomingEmailRepository incomingEmails,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (evt.TenantId == Guid.Empty || evt.FileIds.Count == 0)
            return;

        var claimed = evt.FileIds.ToHashSet();
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        foreach (var draft in await drafts.ListSentByAttachmentFileIdsAsync(evt.TenantId, claimed, ct))
        foreach (var attachment in draft.Attachments.Where(a => claimed.Contains(a.FileId)))
            await bus.PublishAsync(SendDraftHandler.AttachmentSource(draft, attachment.FileId, correlationId));

        foreach (var email in await incomingEmails.ListByStoredAttachmentFileIdsAsync(evt.TenantId, claimed, ct))
        foreach (var attachment in email.Attachments)
            if (attachment.CloudStorageFileId is { } fileId && claimed.Contains(fileId))
                await bus.PublishAsync(IncomingFileSource.Declare(email, fileId, correlationId));
    }
}
