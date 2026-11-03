using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Messages;

// CloudStorage descartó la copia de un adjunto que pidió DownloadAttachmentHandler: ese FileId nunca
// existirá. El adjunto vuelve a Failed sin archivo para que el usuario reintente la descarga.
// Pedidos de otros servicios o FileIds que ya no apunta ningún adjunto → no-op.
public static class AttachmentSaveRejectedConsumer
{
    public static async Task Handle(
        FileSaveRejectedIntegrationEvent evt,
        IIncomingEmailRepository incomingEmails,
        IUnitOfWork unitOfWork,
        ILogger<IncomingEmailAttachment> logger,
        CancellationToken ct
    )
    {
        if (evt.RequestingService != DownloadAttachmentHandler.RequestingService)
            return;

        var email = await incomingEmails.FindByAttachmentCloudStorageFileIdAsync(evt.TenantId, evt.FileId, ct);
        var attachment = email?.Attachments.FirstOrDefault(a => a.CloudStorageFileId == evt.FileId);
        if (attachment is null || !attachment.ReleaseRejectedFile(evt.FileId, evt.Reason))
        {
            logger.LogInformation(
                "CloudStorage rejected file {FileId} ({Reason}) but no incoming attachment points to it.",
                evt.FileId,
                evt.Reason
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning(
            "Incoming attachment {AttachmentId} released: CloudStorage rejected file {FileId} ({Reason}).",
            attachment.Id,
            evt.FileId,
            evt.Reason
        );
    }
}
