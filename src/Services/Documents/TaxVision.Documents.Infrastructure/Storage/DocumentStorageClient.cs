using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using TaxVision.Documents.Application.Abstractions;
using Wolverine;

namespace TaxVision.Documents.Infrastructure.Storage;

/// <summary>
/// Sube el archivo generado al bucket temporal con IAM MinIO PROPIA (scoped a
/// taxvision-temp/documents/*, nunca las root de CloudStorage) y publica SaveFileRequestedIntegrationEvent
/// para que CloudStorage lo registre + escanee y lo almacene de forma permanente. Documents nunca guarda
/// bytes. El FileId lo genera Documents (lo fija en la generación antes de subir) y sirve de idempotencia:
/// un redelivery con el mismo FileId choca contra la unique constraint de CloudStorage y es no-op.
///
/// Mismo patrón (Fase D0/D1) que SignatureCloudStorageClient.UploadAsync.
/// </summary>
public sealed class DocumentStorageClient(
    IMinioClient minioClient,
    IOptions<DocumentsMinioOptions> options,
    IMessageBus bus,
    ILogger<DocumentStorageClient> logger
) : IDocumentStorageClient
{
    public async Task<Result> RequestSaveAsync(
        Guid tenantId,
        Guid fileId,
        byte[] content,
        string fileName,
        string contentType,
        string ownerType,
        Guid ownerId,
        string folderType,
        int? taxYear,
        Guid actorId,
        string correlationId,
        CancellationToken ct = default
    )
    {
        var opt = options.Value;
        var sourceObjectKey = $"{opt.SourcePrefix}/{fileId:N}/{fileName}";

        try
        {
            using var stream = new MemoryStream(content);
            await minioClient.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(opt.TempBucket)
                    .WithObject(sourceObjectKey)
                    .WithStreamData(stream)
                    .WithObjectSize(content.LongLength)
                    .WithContentType(contentType),
                ct
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MinIO PUT failed for generated document {FileName} ({FileId}).", fileName, fileId);
            return Result.Failure(new Error("Documents.Storage.Upload", "MinIO PUT failed."));
        }

        await bus.PublishAsync(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = correlationId,
                FileId = fileId,
                RequestingService = "documents",
                SourceBucket = opt.TempBucket,
                SourceObjectKey = sourceObjectKey,
                ActorId = actorId,
                OwnerType = ownerType,
                OwnerId = ownerId,
                FolderType = folderType,
                TaxYear = taxYear,
                OriginalName = fileName,
                ContentType = contentType,
                SizeBytes = content.LongLength,
            }
        );

        return Result.Success();
    }
}

/// <summary>
/// Credenciales MinIO propias de Documents (IAM scoped a s3:PutObject en taxvision-temp/documents/*,
/// ver deploy/docker/minio/policies/documents-source.json). Nunca las credenciales root de CloudStorage.
/// </summary>
public sealed class DocumentsMinioOptions
{
    public const string SectionName = "Documents:Minio";

    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string TempBucket { get; set; } = "taxvision-temp";
    public string SourcePrefix { get; set; } = "documents";
}
