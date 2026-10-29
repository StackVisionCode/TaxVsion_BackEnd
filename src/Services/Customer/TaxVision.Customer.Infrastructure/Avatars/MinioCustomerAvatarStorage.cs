using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Infrastructure.Imports;

namespace TaxVision.Customer.Infrastructure.Avatars;

/// <summary>
/// PUT de la foto al bucket temporal con la cuenta IAM <c>customer-worker</c> (solo s3:PutObject en
/// <c>taxvision-temp/customer/*</c>). La clave cae dentro de ese prefijo, así no hace falta otra política.
/// CloudStorage la copia a su clave canónica y borra esta.
/// </summary>
internal sealed class MinioCustomerAvatarStorage(
    IMinioClient minioClient,
    IOptions<CustomerMinioOptions> minioOptions,
    ILogger<MinioCustomerAvatarStorage> logger
) : ICustomerAvatarStorage
{
    public async Task<Result<CustomerAvatarSourceObject>> StoreAsync(
        Guid tenantId,
        Guid fileId,
        byte[] content,
        string contentType,
        CancellationToken ct
    )
    {
        var options = minioOptions.Value;
        var objectKey = ObjectKeyFor(options.SourcePrefix, tenantId, fileId);

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            await minioClient.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(options.TempBucket)
                    .WithObject(objectKey)
                    .WithStreamData(stream)
                    .WithObjectSize(content.LongLength)
                    .WithContentType(contentType),
                ct
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "MinIO PUT failed for customer avatar {FileId} (tenant {TenantId}).",
                fileId,
                tenantId
            );
            return Result.Failure<CustomerAvatarSourceObject>(
                new Error("Customer.Avatar.Storage.Upload", "MinIO PUT failed.")
            );
        }

        return Result.Success(new CustomerAvatarSourceObject(options.TempBucket, objectKey));
    }

    internal static string ObjectKeyFor(string sourcePrefix, Guid tenantId, Guid fileId) =>
        $"{sourcePrefix}/avatars/{tenantId:N}/{fileId:N}.webp";
}
