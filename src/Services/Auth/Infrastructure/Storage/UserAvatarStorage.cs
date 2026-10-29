using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.Avatars;
using TaxVision.Auth.Infrastructure.Onboarding.Storage;

namespace TaxVision.Auth.Infrastructure.Storage;

/// <summary>
/// Sube la foto normalizada a <c>taxvision-temp/{SourcePrefix}/avatars/...</c> con la cuenta IAM de Auth
/// (<c>auth-worker</c>, solo s3:PutObject sobre <c>taxvision-temp/auth/*</c>). CloudStorage la copia al
/// bucket principal cuando consume el SaveFileRequested que publica el handler.
/// </summary>
public sealed class UserAvatarStorage(
    IMinioClient minioClient,
    IOptions<AuthMinioOptions> minioOptions,
    ILogger<UserAvatarStorage> logger
) : IUserAvatarStorage
{
    public async Task<Result<StoredUserAvatar>> StoreAsync(
        Guid tenantId,
        Guid userId,
        Guid fileId,
        byte[] content,
        string contentType,
        CancellationToken ct = default
    )
    {
        var options = minioOptions.Value;
        var objectKey =
            $"{options.SourcePrefix}/avatars/{tenantId:N}/{userId:N}/{fileId:N}/{UserAvatarMessages.FileName}";

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
            logger.LogWarning(ex, "MinIO PUT failed for user {UserId} avatar {FileId}.", userId, fileId);
            return Result.Failure<StoredUserAvatar>(UserAvatarErrors.StorageUnavailable);
        }

        return Result.Success(new StoredUserAvatar(fileId, options.TempBucket, objectKey));
    }
}
