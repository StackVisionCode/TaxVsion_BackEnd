using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using TaxVision.Documents.Application.Abstractions;

namespace TaxVision.Documents.Infrastructure.Storage;

/// <summary>
/// Sube el archivo generado al bucket temporal con IAM MinIO PROPIA (scoped a
/// taxvision-temp/documents/*, nunca las root de CloudStorage). Documents nunca guarda bytes. El FileId lo
/// genera Documents (lo fija en la generación antes de subir). SaveFileRequestedIntegrationEvent lo publica
/// el handler: un bus inyectado aquí sale del contenedor y no espera el commit (DOC-3).
/// </summary>
public sealed class DocumentStorageClient(
    IMinioClient minioClient,
    IOptions<DocumentsMinioOptions> options,
    ILogger<DocumentStorageClient> logger
) : IDocumentStorageClient
{
    public async Task<Result<StorageUploadResult>> UploadAsync(
        Guid fileId,
        byte[] content,
        string fileName,
        string contentType,
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
            return Result.Failure<StorageUploadResult>(new Error("Documents.Storage.Upload", "MinIO PUT failed."));
        }

        return Result.Success(
            new StorageUploadResult(fileId, opt.TempBucket, sourceObjectKey, content.LongLength, contentType)
        );
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
