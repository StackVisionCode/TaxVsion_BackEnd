using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Descarga bajo demanda de un attachment (Fase 8) — HTTP-triggered, no un consumer Wolverine
/// (mismo criterio que <see cref="GetMessageBodyHandler"/>: no empuja correlación, ya viene
/// pusheada por <c>CorrelationIdMiddleware</c>). Orquesta: cargar → chequeo de idempotencia →
/// marcar en progreso → pedir bytes a Connectors → subir al bucket temporal → publicar
/// <see cref="SaveFileRequestedIntegrationEvent"/> (patrón D0/D1, ver
/// <see cref="ICorrespondenceTempBucketUploader"/>) → marcar descargado. Cualquier falla en el
/// camino marca el attachment como <see cref="AttachmentDownloadStatus.Failed"/> antes de
/// devolver el error — nunca lo deja colgado en <see cref="AttachmentDownloadStatus.InProgress"/>.
///
/// <para>
/// Sin transacción propia (COR-08): Connectors y MinIO tardan hasta 30 s cada uno, con la fila bloqueada. InProgress
/// nunca fue visible fuera de esa transacción, así que no se guarda: un corte a mitad no deja el adjunto colgado.
/// </para>
/// </summary>
public static class DownloadAttachmentHandler
{
    // Firma del pedido a CloudStorage; AttachmentSaveRejectedConsumer filtra los rechazos con este valor.
    public const string RequestingService = "correspondence";

    [NonTransactional]
    public static async Task<Result<DownloadAttachmentResult>> Handle(
        DownloadAttachmentCommand command,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository emailThreads,
        IConnectorsClient connectorsClient,
        ICorrespondenceTempBucketUploader tempBucketUploader,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var loadResult = await LoadAsync(command, incomingEmails, emailThreads, ct);
        if (loadResult.IsFailure)
            return Result.Failure<DownloadAttachmentResult>(loadResult.Error);
        var (email, attachment) = loadResult.Value;

        if (attachment.DownloadStatus == AttachmentDownloadStatus.Blocked)
            return Result.Failure<DownloadAttachmentResult>(
                new Error("IncomingEmailAttachment.Blocked", "This attachment was blocked by the security scan.")
            );

        if (attachment.DownloadStatus == AttachmentDownloadStatus.UnderReview)
            return Result.Failure<DownloadAttachmentResult>(
                new Error(
                    "IncomingEmailAttachment.UnderReview",
                    "This attachment is under review against our content policy."
                )
            );

        if (attachment.DownloadStatus == AttachmentDownloadStatus.Downloaded)
            return Result.Success(ToResult(attachment));

        // Storage lo rechazaría igual: no se vuelve a bajar ni a copiar. Cuota/suspensión sí reintentan.
        if (attachment.HasPermanentStorageRejection)
            return Result.Failure<DownloadAttachmentResult>(AttachmentFailureErrors.For(attachment));

        var markInProgressResult = attachment.MarkInProgress();
        if (markInProgressResult.IsFailure)
            return Result.Failure<DownloadAttachmentResult>(markInProgressResult.Error);

        var fetchResult = await FetchBytesAsync(command, email, attachment, connectorsClient, ct);
        if (fetchResult.IsFailure)
            return await FailAsync(attachment, fetchResult.Error, unitOfWork, ct);

        // Un único FileId para todo el flujo — el mismo que arma la key del objeto temporal es
        // el que CloudStorage va a registrar como Id definitivo (ver SaveFileRequestedIntegrationEvent,
        // mismo criterio que SignatureCloudStorageClient.UploadAsync).
        var fileId = Guid.NewGuid();
        var uploadResult = await UploadToTempBucketAsync(fileId, attachment, fetchResult.Value, tempBucketUploader, ct);
        if (uploadResult.IsFailure)
            return await FailAsync(attachment, uploadResult.Error, unitOfWork, ct);

        return await bus.InvokeAsync<Result<DownloadAttachmentResult>>(
            new CompleteAttachmentDownloadCommand(
                command.TenantId,
                email.Id,
                attachment.Id,
                command.ActorId,
                fileId,
                uploadResult.Value.SourceBucket,
                uploadResult.Value.SourceObjectKey,
                fetchResult.Value.Content.LongLength,
                correlation.CorrelationId
            ),
            ct
        );
    }

    private static async Task<Result<(IncomingEmail Email, IncomingEmailAttachment Attachment)>> LoadAsync(
        DownloadAttachmentCommand command,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository emailThreads,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(command.TenantId, command.IncomingEmailId, ct);
        if (email is null)
            return Result.Failure<(IncomingEmail, IncomingEmailAttachment)>(
                new Error("IncomingEmail.NotFound", "The message was not found for this tenant.")
            );

        // A1 — mismo gate y misma respuesta que el cuerpo del mensaje: si el buzón no es visible, el
        // adjunto "no existe". Un 403 confirmaría que ese correo está en el tenant.
        if (
            !await MailboxVisibility.CanSeeMessageAsync(
                command.VisibleAccountIds,
                command.TenantId,
                email,
                emailThreads,
                ct
            )
        )
            return Result.Failure<(IncomingEmail, IncomingEmailAttachment)>(
                new Error("IncomingEmail.NotFound", "The message was not found for this tenant.")
            );

        var attachment = email.Attachments.FirstOrDefault(a => a.Id == command.AttachmentId);
        return attachment is null
            ? Result.Failure<(IncomingEmail, IncomingEmailAttachment)>(
                new Error("IncomingEmailAttachment.NotFound", "The attachment was not found on this message.")
            )
            : Result.Success((email, attachment));
    }

    private static Task<Result<ConnectorsAttachmentBytes>> FetchBytesAsync(
        DownloadAttachmentCommand command,
        IncomingEmail email,
        IncomingEmailAttachment attachment,
        IConnectorsClient connectorsClient,
        CancellationToken ct
    ) =>
        connectorsClient.FetchAttachmentAsync(
            command.TenantId,
            email.AccountId,
            email.ProviderMessageId,
            attachment.ProviderAttachmentId,
            attachment.Filename,
            attachment.SizeBytes,
            attachment.PartId,
            ct
        );

    private static Task<Result<TempBucketUploadResult>> UploadToTempBucketAsync(
        Guid fileId,
        IncomingEmailAttachment attachment,
        ConnectorsAttachmentBytes bytes,
        ICorrespondenceTempBucketUploader tempBucketUploader,
        CancellationToken ct
    ) => tempBucketUploader.UploadAsync(fileId, bytes.Content, attachment.Filename, attachment.ContentType, ct);

    /// <summary>
    /// Marca el attachment como Failed y persiste antes de propagar el error original al caller
    /// HTTP. <see cref="IncomingEmailAttachment.MarkFailed"/> solo puede rechazar la transición si
    /// el estado ya cambió por fuera de este flujo (no debería pasar acá, pero se chequea igual —
    /// mismo criterio que <see cref="RawMessageReceivedConsumer"/> nunca descarta un <c>Result</c>
    /// de dominio en silencio); si eso ocurre, prevalece el error original, no el de la transición.
    /// </summary>
    private static async Task<Result<DownloadAttachmentResult>> FailAsync(
        IncomingEmailAttachment attachment,
        Error error,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var markFailedResult = attachment.MarkFailed($"{error.Code}: {error.Message}");
        if (markFailedResult.IsSuccess)
            await unitOfWork.SaveChangesAsync(ct);

        return Result.Failure<DownloadAttachmentResult>(error);
    }

    internal static DownloadAttachmentResult ToResult(IncomingEmailAttachment attachment) =>
        new(attachment.Id, attachment.DownloadStatus.ToString(), attachment.CloudStorageFileId!.Value);
}
