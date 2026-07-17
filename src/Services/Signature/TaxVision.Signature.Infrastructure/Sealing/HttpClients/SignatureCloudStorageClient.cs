using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using TaxVision.Signature.Application.Abstractions.Sealing;
using Wolverine;

namespace TaxVision.Signature.Infrastructure.Sealing.HttpClients;

/// <summary>
/// Implementación de <see cref="ISignatureCloudStorageClient"/>.
///
/// <para>
/// DownloadAsync sigue el flujo HTTP+M2M presignado (initiate → GET, ver
/// <see cref="ISignatureServiceTokenAcquirer"/>) — fuera del scope de la Fase D1, ver
/// project_cloudstorage_hardening_plan.md. UploadAsync ya NO llama a CloudStorage por
/// HTTP (Fase D1): sube el objeto directo a MinIO con credenciales propias (IAM
/// scoped a taxvision-temp/signature/*) y publica SaveFileRequestedIntegrationEvent
/// para que CloudStorage lo registre y escanee de forma asincrona — el mismo patron
/// que ya usa el flujo normal de subida (initiate → complete → scan async).
/// </para>
///
/// <para>Estructurado con métodos privados por fase para mantener SRP.</para>
/// </summary>
internal sealed class SignatureCloudStorageClient(
    HttpClient httpClient,
    ISignatureServiceTokenAcquirer tokenAcquirer,
    IMinioClient minioClient,
    IOptions<SignatureMinioOptions> minioOptions,
    IMessageBus bus,
    ICorrelationContext correlation,
    ILogger<SignatureCloudStorageClient> logger
) : ISignatureCloudStorageClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        var tokenResult = await AcquireTokenAsync(tenantId, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<byte[]>(tokenResult.Error);

        var urlResult = await FetchDownloadUrlAsync(fileId, tokenResult.Value, ct);
        if (urlResult.IsFailure)
            return Result.Failure<byte[]>(urlResult.Error);

        return await FetchBytesAsync(urlResult.Value, ct);
    }

    public async Task<Result<Guid>> UploadAsync(
        Guid tenantId,
        SignaturePdfUpload upload,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(upload);

        var fileId = Guid.NewGuid();
        var options = minioOptions.Value;
        var sourceObjectKey = $"{options.SourcePrefix}/{fileId:N}/{upload.FileName}";

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
            logger.LogWarning(ex, "MinIO PUT failed for sealed document upload ({FileName}).", upload.FileName);
            return Result.Failure<Guid>(new Error("Signature.Storage.Upload", "MinIO PUT failed."));
        }

        await bus.PublishAsync(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = tenantId,
                FileId = fileId,
                RequestingService = "signature",
                SourceBucket = options.TempBucket,
                SourceObjectKey = sourceObjectKey,
                ActorId = upload.ActorId,
                OwnerType = upload.OwnerType,
                OwnerId = upload.OwnerId,
                FolderType = upload.FolderType,
                TaxYear = upload.TaxYear,
                OriginalName = upload.FileName,
                ContentType = upload.ContentType,
                SizeBytes = upload.Content.LongLength,
                CorrelationId = correlation.CorrelationId,
            }
        );

        return Result.Success(fileId);
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno con una única responsabilidad
    // ------------------------------------------------------------------

    private async Task<Result<string>> AcquireTokenAsync(Guid tenantId, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        return string.IsNullOrEmpty(token)
            ? Result.Failure<string>(new Error("Signature.Storage.Auth", "No CloudStorage credentials available."))
            : Result.Success(token);
    }

    private async Task<Result<Uri>> FetchDownloadUrlAsync(Guid fileId, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/download-url");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("CloudStorage download-url call failed ({Status}).", (int)response.StatusCode);
            return Result.Failure<Uri>(new Error("Signature.Storage.Download", "download-url request failed."));
        }
        var payload = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(Json, ct);
        return payload is null
            ? Result.Failure<Uri>(new Error("Signature.Storage.Download", "Empty response from download-url."))
            : Result.Success(payload.DownloadUrl);
    }

    private async Task<Result<byte[]>> FetchBytesAsync(Uri url, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("MinIO presigned download failed ({Status}).", (int)response.StatusCode);
            return Result.Failure<byte[]>(new Error("Signature.Storage.Download", "presigned download failed."));
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return Result.Success(bytes);
    }

    // ==================== DTOs privados ====================

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
