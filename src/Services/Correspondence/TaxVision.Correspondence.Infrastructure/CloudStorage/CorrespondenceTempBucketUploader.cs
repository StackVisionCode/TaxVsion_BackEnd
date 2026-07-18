using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Infrastructure.CloudStorage;

/// <summary>
/// Implementación de <see cref="ICorrespondenceTempBucketUploader"/> — sube el objeto directo a
/// MinIO con credenciales propias (IAM scoped a taxvision-temp/correspondence/*), mismo patrón
/// D0/D1 que <c>SignatureCloudStorageClient.UploadAsync</c>. No publica ningún evento: eso es
/// responsabilidad de <see cref="Messages.DownloadAttachmentHandler"/> (SRP).
/// </summary>
internal sealed class CorrespondenceTempBucketUploader(
    IMinioClient minioClient,
    IOptions<CorrespondenceMinioOptions> options,
    ILogger<CorrespondenceTempBucketUploader> logger
) : ICorrespondenceTempBucketUploader
{
    public async Task<Result<TempBucketUploadResult>> UploadAsync(
        Guid fileId,
        byte[] content,
        string filename,
        string contentType,
        CancellationToken ct = default
    )
    {
        var opt = options.Value;
        var sourceObjectKey = $"{opt.SourcePrefix}/{fileId:N}/{filename}";

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
            logger.LogWarning(ex, "MinIO PUT failed for correspondence attachment upload ({Filename}).", filename);
            return Result.Failure<TempBucketUploadResult>(
                new Error("CorrespondenceTempBucketUploader.UploadFailed", "MinIO PUT failed.")
            );
        }

        return Result.Success(new TempBucketUploadResult(opt.TempBucket, sourceObjectKey));
    }
}
