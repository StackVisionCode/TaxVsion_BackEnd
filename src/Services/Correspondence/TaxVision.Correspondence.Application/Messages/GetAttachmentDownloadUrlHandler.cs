using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Lectura bajo demanda (Fase 8) — HTTP-triggered, no un consumer Wolverine (mismo criterio que
/// <see cref="GetMessageBodyHandler"/>/<see cref="ListMessageAttachmentsHandler"/>: no empuja
/// correlación). Nunca dispara la descarga por su cuenta — si el attachment todavía no está
/// <see cref="AttachmentDownloadStatus.Downloaded"/>, devuelve un error 409 "todavía no está
/// listo" en vez de descargar implícitamente (plan §22/§25: son dos endpoints separados a
/// propósito).
/// </summary>
public static class GetAttachmentDownloadUrlHandler
{
    public static async Task<Result<AttachmentDownloadUrlResult>> Handle(
        GetAttachmentDownloadUrlQuery query,
        IIncomingEmailRepository incomingEmails,
        ICloudStorageClient cloudStorageClient,
        CancellationToken ct
    )
    {
        var attachmentResult = await LoadReadyAttachmentAsync(query, incomingEmails, ct);
        if (attachmentResult.IsFailure)
            return Result.Failure<AttachmentDownloadUrlResult>(attachmentResult.Error);
        var attachment = attachmentResult.Value;

        var urlResult = await cloudStorageClient.GetDownloadUrlAsync(
            query.TenantId,
            attachment.CloudStorageFileId!.Value,
            ct
        );
        return urlResult.IsFailure
            ? Result.Failure<AttachmentDownloadUrlResult>(urlResult.Error)
            : Result.Success(
                new AttachmentDownloadUrlResult(
                    attachment.Id,
                    urlResult.Value.DownloadUrl,
                    urlResult.Value.ExpiresAtUtc
                )
            );
    }

    private static async Task<Result<IncomingEmailAttachment>> LoadReadyAttachmentAsync(
        GetAttachmentDownloadUrlQuery query,
        IIncomingEmailRepository incomingEmails,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(query.TenantId, query.IncomingEmailId, ct);
        if (email is null)
            return Result.Failure<IncomingEmailAttachment>(
                new Error("IncomingEmail.NotFound", "The message was not found for this tenant.")
            );

        var attachment = email.Attachments.FirstOrDefault(a => a.Id == query.AttachmentId);
        if (attachment is null)
            return Result.Failure<IncomingEmailAttachment>(
                new Error("IncomingEmailAttachment.NotFound", "The attachment was not found on this message.")
            );

        return attachment.DownloadStatus != AttachmentDownloadStatus.Downloaded
            ? Result.Failure<IncomingEmailAttachment>(
                new Error(
                    "IncomingEmailAttachment.NotReady",
                    $"The attachment is not downloaded yet (status: {attachment.DownloadStatus})."
                )
            )
            : Result.Success(attachment);
    }
}
