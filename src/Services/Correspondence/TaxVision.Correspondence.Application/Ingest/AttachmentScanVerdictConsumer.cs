using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Ingest;

// CloudStorage escaneó un binario y lo marcó peligroso. Si es un adjunto entrante, se marca Blocked
// para no ofrecerlo en descarga. No es un adjunto entrante (otro dueño) → no-op.
public static class AttachmentScanVerdictConsumer
{
    public static Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        IIncomingEmailRepository incomingEmails,
        IUnitOfWork unitOfWork,
        ILogger<IncomingEmailAttachment> logger,
        CancellationToken ct
    ) => BlockAsync(evt.TenantId, evt.FileId, "Virus detected", incomingEmails, unitOfWork, logger, ct);

    public static Task Handle(
        FileBlockedByPolicyIntegrationEvent evt,
        IIncomingEmailRepository incomingEmails,
        IUnitOfWork unitOfWork,
        ILogger<IncomingEmailAttachment> logger,
        CancellationToken ct
    ) => BlockAsync(evt.TenantId, evt.FileId, "Blocked by content policy", incomingEmails, unitOfWork, logger, ct);

    private static async Task BlockAsync(
        Guid tenantId,
        Guid fileId,
        string reason,
        IIncomingEmailRepository incomingEmails,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.FindByAttachmentCloudStorageFileIdAsync(tenantId, fileId, ct);
        var attachment = email?.Attachments.FirstOrDefault(a => a.CloudStorageFileId == fileId);
        if (attachment is null)
            return;

        attachment.MarkBlocked(reason);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning("Incoming attachment {AttachmentId} blocked by scan: {Reason}.", attachment.Id, reason);
    }
}
