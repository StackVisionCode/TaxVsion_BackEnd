using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;
using Wolverine;

namespace TaxVision.Correspondence.Application.Messages;

/// <summary>Marca el adjunto ya subido al bucket temporal y pide guardarlo en la misma transacción (outbox).</summary>
public sealed record CompleteAttachmentDownloadCommand(
    Guid TenantId,
    Guid MessageId,
    Guid AttachmentId,
    Guid ActorId,
    Guid FileId,
    string SourceBucket,
    string SourceObjectKey,
    long SizeBytes,
    string CorrelationId
);

public static class CompleteAttachmentDownloadHandler
{
    public static async Task<Result<DownloadAttachmentResult>> Handle(
        CompleteAttachmentDownloadCommand command,
        IIncomingEmailRepository incomingEmails,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(command.TenantId, command.MessageId, ct);
        var attachment = email?.Attachments.FirstOrDefault(a => a.Id == command.AttachmentId);
        if (email is null || attachment is null)
            return Result.Failure<DownloadAttachmentResult>(
                new Error("IncomingEmailAttachment.NotFound", "The attachment was not found for this message.")
            );

        // Otra petición ya la terminó mientras esta bajaba los bytes.
        if (attachment.DownloadStatus == AttachmentDownloadStatus.Downloaded)
            return Result.Success(DownloadAttachmentHandler.ToResult(attachment));

        if (attachment.DownloadStatus != AttachmentDownloadStatus.InProgress)
        {
            var start = attachment.MarkInProgress();
            if (start.IsFailure)
                return Result.Failure<DownloadAttachmentResult>(start.Error);
        }

        var mark = attachment.MarkDownloaded(command.FileId);
        if (mark.IsFailure)
            return Result.Failure<DownloadAttachmentResult>(mark.Error);

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = command.FileId,
                RequestingService = DownloadAttachmentHandler.RequestingService,
                SourceBucket = command.SourceBucket,
                SourceObjectKey = command.SourceObjectKey,
                ActorId = command.ActorId,
                // Sin cliente queda a nombre de quien lo bajó, como los adjuntos de un borrador sin cliente.
                OwnerType = email.CustomerId is null ? "User" : "Customer",
                OwnerId = email.CustomerId ?? command.ActorId,
                FolderType = "EmailIncoming",
                TaxYear = email.ReceivedAtUtc.Year,
                OriginalName = attachment.Filename,
                ContentType = attachment.ContentType,
                SizeBytes = command.SizeBytes,
                CorrelationId = command.CorrelationId,
            }
        );

        return Result.Success(DownloadAttachmentHandler.ToResult(attachment));
    }
}
