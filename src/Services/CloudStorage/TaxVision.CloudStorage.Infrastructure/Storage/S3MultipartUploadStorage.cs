using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>
/// Fase U — implementa IMultipartUploadStorage con AWSSDK.S3 (no el SDK "Minio").
/// Verificado contra un MinIO real: `GetPreSignedUrlRequest` tiene propiedades de
/// primera clase UploadId/PartNumber/Protocol — usarlas es obligatorio, el bag
/// generico `.Parameters.Add("partNumber", ...)` le agrega un prefijo "x-" que
/// MinIO ignora silenciosamente (la "parte" sube con 200 OK pero pisa el objeto
/// entero en vez de registrarse en el multipart upload; CompleteMultipartUpload
/// despues falla con "part not found"). `AmazonS3Config.UseHttp`/`SignatureVersion`
/// tampoco los respeta GetPreSignedURLAsync — el esquema hay que forzarlo por
/// request via `.Protocol`.
/// </summary>
public sealed class S3MultipartUploadStorage(IAmazonS3 client, IOptions<MinioOptions> minioOptions)
    : IMultipartUploadStorage
{
    public async Task<MultipartUploadInitiation> InitiateAsync(
        string bucket,
        string objectKey,
        string contentType,
        long totalSizeBytes,
        long partSizeBytes,
        TimeSpan urlLifetime,
        CancellationToken ct
    )
    {
        var initiated = await client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest
            {
                BucketName = bucket,
                Key = objectKey,
                ContentType = contentType,
            },
            ct
        );

        var protocol = minioOptions.Value.UseTls ? Protocol.HTTPS : Protocol.HTTP;
        var expires = DateTime.UtcNow.Add(urlLifetime);
        var partCount = (int)Math.Ceiling((double)totalSizeBytes / partSizeBytes);
        var parts = new List<MultipartPartUploadUrl>(partCount);
        for (var partNumber = 1; partNumber <= partCount; partNumber++)
        {
            var url = await client.GetPreSignedURLAsync(
                new GetPreSignedUrlRequest
                {
                    BucketName = bucket,
                    Key = objectKey,
                    Verb = HttpVerb.PUT,
                    Expires = expires,
                    UploadId = initiated.UploadId,
                    PartNumber = partNumber,
                    Protocol = protocol,
                }
            );
            parts.Add(new MultipartPartUploadUrl(partNumber, new Uri(url)));
        }

        return new MultipartUploadInitiation(initiated.UploadId, parts);
    }

    public Task CompleteAsync(
        string bucket,
        string objectKey,
        string uploadId,
        IReadOnlyList<MultipartPart> parts,
        CancellationToken ct
    )
    {
        var request = new CompleteMultipartUploadRequest
        {
            BucketName = bucket,
            Key = objectKey,
            UploadId = uploadId,
        };
        // S3 exige orden ascendente de partNumber al ensamblar.
        foreach (var part in parts.OrderBy(p => p.PartNumber))
            request.PartETags.Add(new PartETag(part.PartNumber, part.ETag));
        return client.CompleteMultipartUploadAsync(request, ct);
    }

    public Task AbortAsync(string bucket, string objectKey, string uploadId, CancellationToken ct) =>
        client.AbortMultipartUploadAsync(
            new AbortMultipartUploadRequest
            {
                BucketName = bucket,
                Key = objectKey,
                UploadId = uploadId,
            },
            ct
        );
}
