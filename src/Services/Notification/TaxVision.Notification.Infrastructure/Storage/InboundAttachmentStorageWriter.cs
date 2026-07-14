using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using TaxVision.Notification.Application.Abstractions;
using Wolverine;

namespace TaxVision.Notification.Infrastructure.Storage;

/// <summary>
/// Fase D3 — sube el adjunto de un email sincronizado por IMAP directo a MinIO
/// (credenciales propias, IAM scoped a taxvision-temp/notification/*) y publica
/// <see cref="SaveFileRequestedIntegrationEvent"/> — Notification es un Wolverine
/// nativo, asi que a diferencia de CommunicationTranscriptWorker (Fase D2, Node) no
/// necesita una cola dedicada: publica al fanout "taxvision-events" normal, y
/// CloudStorage lo recibe por su listener Wolverine ya existente (envelope real, sin
/// necesidad de DefaultIncomingMessage).
/// </summary>
public sealed class InboundAttachmentStorageWriter(
    IMinioClient minioClient,
    IOptions<NotificationMinioOptions> minioOptions,
    IMessageBus bus,
    ICorrelationContext correlation,
    ILogger<InboundAttachmentStorageWriter> logger
) : IInboundAttachmentStorageWriter
{
    public async Task<Result<Guid>> SaveAsync(
        InboundAttachmentUpload upload,
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        var options = minioOptions.Value;
        var fileId = Guid.NewGuid();
        var sourceObjectKey = $"{options.SourcePrefix}/{fileId:N}/{upload.OriginalName}";

        try
        {
            using var content = new MemoryStream(upload.Content);
            await minioClient.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(options.TempBucket)
                    .WithObject(sourceObjectKey)
                    .WithStreamData(content)
                    .WithObjectSize(upload.Content.LongLength)
                    .WithContentType(upload.ContentType),
                ct
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MinIO PUT failed for inbound attachment ({FileName}).", upload.OriginalName);
            return Result.Failure<Guid>(new Error("Email.Storage.Upload", "MinIO PUT failed."));
        }

        await bus.PublishAsync(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = tenantId,
                FileId = fileId,
                RequestingService = "notification",
                SourceBucket = options.TempBucket,
                SourceObjectKey = sourceObjectKey,
                // Sin actor humano disponible — el trigger es un sync IMAP en background,
                // no una accion de usuario (mismo criterio/valor que CommunicationTranscriptWorker, Fase D2).
                ActorId = Guid.Empty,
                OwnerType = "Communication",
                OwnerId = upload.MessageId,
                FolderType = "EmailIncoming",
                TaxYear = upload.TaxYear,
                OriginalName = upload.OriginalName,
                ContentType = upload.ContentType,
                SizeBytes = upload.Content.LongLength,
                CorrelationId = correlation.CorrelationId,
            }
        );

        return Result.Success(fileId);
    }
}
