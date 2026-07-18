using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;
using Wolverine;

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
/// </summary>
public static class DownloadAttachmentHandler
{
    public static async Task<Result<DownloadAttachmentResult>> Handle(
        DownloadAttachmentCommand command,
        IIncomingEmailRepository incomingEmails,
        IConnectorsClient connectorsClient,
        ICorrespondenceTempBucketUploader tempBucketUploader,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var loadResult = await LoadAsync(command, incomingEmails, ct);
        if (loadResult.IsFailure)
            return Result.Failure<DownloadAttachmentResult>(loadResult.Error);
        var (email, attachment) = loadResult.Value;

        if (attachment.DownloadStatus == AttachmentDownloadStatus.Downloaded)
            return Result.Success(ToResult(attachment));

        var markInProgressResult = await MarkInProgressAsync(attachment, unitOfWork, ct);
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

        var markDownloadedResult = await PublishAndMarkDownloadedAsync(
            command,
            email,
            attachment,
            uploadResult.Value,
            fileId,
            fetchResult.Value.Content.LongLength,
            bus,
            correlation,
            unitOfWork,
            ct
        );
        if (markDownloadedResult.IsFailure)
            return Result.Failure<DownloadAttachmentResult>(markDownloadedResult.Error);

        return Result.Success(ToResult(attachment));
    }

    private static async Task<Result<(IncomingEmail Email, IncomingEmailAttachment Attachment)>> LoadAsync(
        DownloadAttachmentCommand command,
        IIncomingEmailRepository incomingEmails,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(command.TenantId, command.IncomingEmailId, ct);
        if (email is null)
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

    private static async Task<Result> MarkInProgressAsync(
        IncomingEmailAttachment attachment,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var markResult = attachment.MarkInProgress();
        if (markResult.IsFailure)
            return markResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
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
    /// Publica <see cref="SaveFileRequestedIntegrationEvent"/> y marca el attachment como
    /// descargado en la MISMA transacción (outbox de Wolverine): si el commit falla, el evento
    /// nunca sale y el attachment queda como estaba, listo para reintentar.
    /// </summary>
    private static async Task<Result> PublishAndMarkDownloadedAsync(
        DownloadAttachmentCommand command,
        IncomingEmail email,
        IncomingEmailAttachment attachment,
        TempBucketUploadResult upload,
        Guid fileId,
        long sizeBytes,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await bus.PublishAsync(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = command.TenantId,
                FileId = fileId,
                RequestingService = "correspondence",
                SourceBucket = upload.SourceBucket,
                SourceObjectKey = upload.SourceObjectKey,
                ActorId = command.ActorId,
                OwnerType = "Customer",
                OwnerId = email.CustomerId,
                FolderType = "EmailIncoming",
                TaxYear = email.ReceivedAtUtc.Year,
                OriginalName = attachment.Filename,
                ContentType = attachment.ContentType,
                SizeBytes = sizeBytes,
                CorrelationId = correlation.CorrelationId,
            }
        );

        var markResult = attachment.MarkDownloaded(fileId);
        if (markResult.IsFailure)
            return markResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

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

    private static DownloadAttachmentResult ToResult(IncomingEmailAttachment attachment) =>
        new(attachment.Id, attachment.DownloadStatus.ToString(), attachment.CloudStorageFileId!.Value);
}
