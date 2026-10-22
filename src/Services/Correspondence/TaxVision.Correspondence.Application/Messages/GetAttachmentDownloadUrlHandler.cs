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
    /// <summary>Cuánto puede tardar CloudStorage en registrar la copia antes de que su ausencia cuente como fallo.</summary>
    public static readonly TimeSpan CopyGracePeriod = TimeSpan.FromMinutes(2);

    public static async Task<Result<AttachmentDownloadUrlResult>> Handle(
        GetAttachmentDownloadUrlQuery query,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository emailThreads,
        ICloudStorageClient cloudStorageClient,
        CancellationToken ct
    )
    {
        var attachmentResult = await LoadReadyAttachmentAsync(query, incomingEmails, emailThreads, ct);
        if (attachmentResult.IsFailure)
            return Result.Failure<AttachmentDownloadUrlResult>(attachmentResult.Error);
        var attachment = attachmentResult.Value;

        var urlResult = await cloudStorageClient.GetDownloadUrlAsync(
            query.TenantId,
            attachment.CloudStorageFileId!.Value,
            ct
        );
        if (urlResult.IsFailure && IsCopyStillInFlight(urlResult.Error, attachment))
            return Result.Failure<AttachmentDownloadUrlResult>(
                new Error("IncomingEmailAttachment.NotReady", "The attachment is still being copied to storage.")
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

    // Recién descargado y CloudStorage aún no lo tiene: 409 para que el front siga esperando, no un 404.
    private static bool IsCopyStillInFlight(Error error, IncomingEmailAttachment attachment) =>
        error.Code == "CloudStorageClient.FileNotRegistered"
        && attachment.DownloadedAtUtc is { } downloadedAt
        && DateTime.UtcNow - downloadedAt < CopyGracePeriod;

    private static async Task<Result<IncomingEmailAttachment>> LoadReadyAttachmentAsync(
        GetAttachmentDownloadUrlQuery query,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository emailThreads,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(query.TenantId, query.IncomingEmailId, ct);
        if (email is null)
            return Result.Failure<IncomingEmailAttachment>(
                new Error("IncomingEmail.NotFound", "The message was not found for this tenant.")
            );

        // A1 — gate de buzón, igual que en la descarga: mismo error que un mensaje inexistente.
        if (
            !await MailboxVisibility.CanSeeMessageAsync(
                query.VisibleAccountIds,
                query.TenantId,
                email,
                emailThreads,
                ct
            )
        )
            return Result.Failure<IncomingEmailAttachment>(
                new Error("IncomingEmail.NotFound", "The message was not found for this tenant.")
            );

        var attachment = email.Attachments.FirstOrDefault(a => a.Id == query.AttachmentId);
        if (attachment is null)
            return Result.Failure<IncomingEmailAttachment>(
                new Error("IncomingEmailAttachment.NotFound", "The attachment was not found on this message.")
            );

        if (attachment.DownloadStatus == AttachmentDownloadStatus.Blocked)
            return Result.Failure<IncomingEmailAttachment>(
                new Error("IncomingEmailAttachment.Blocked", "This attachment was blocked by the security scan.")
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
