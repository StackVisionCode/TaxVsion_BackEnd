using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using TaxVision.Scribe.Application.Abstractions;
using Wolverine;

namespace TaxVision.Scribe.Infrastructure.Storage;

/// <summary>
/// Descarga texto de CloudStorage vía el flujo HTTP+M2M presignado (initiate download-url → GET),
/// mismo patrón que Postmaster (ver CloudStorageInlineAssetFetcher). Para templates/layouts System
/// (TenantId null) se usa <see cref="PlatformTenant.Id"/> como identidad M2M: no hay un tenant real
/// dueño de esos archivos, y Auth exige un TenantId no vacío para emitir el service-token. La subida
/// (Fase 5) sigue el patrón D1: PUT directo a MinIO con credenciales propias + publica
/// SaveFileRequestedIntegrationEvent para que CloudStorage catalogue/escanee de forma asíncrona.
/// </summary>
public sealed class CloudStorageClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    IMinioClient minioClient,
    IOptions<ScribeMinioOptions> minioOptions,
    IMessageBus bus,
    ICorrelationContext correlation,
    ILogger<CloudStorageClient> logger
) : ICloudStorageClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<string>> DownloadTextAsync(Guid fileId, Guid? tenantId, CancellationToken ct = default)
    {
        var tokenResult = await AcquireTokenAsync(tenantId ?? PlatformTenant.Id, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<string>(tokenResult.Error);

        var urlResult = await FetchDownloadUrlAsync(fileId, tokenResult.Value, ct);
        if (urlResult.IsFailure)
            return Result.Failure<string>(urlResult.Error);

        return await FetchTextAsync(urlResult.Value, ct);
    }

    public async Task<Result<string>> UploadAsync(
        Guid? tenantId,
        Guid fileId,
        byte[] content,
        string fileName,
        string contentType,
        string folderType,
        Guid actorId,
        CancellationToken ct = default
    )
    {
        var options = minioOptions.Value;
        var sourceObjectKey = $"{options.SourcePrefix}/{fileId:N}/{fileName}";

        try
        {
            using var stream = new MemoryStream(content);
            await minioClient.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(options.TempBucket)
                    .WithObject(sourceObjectKey)
                    .WithStreamData(stream)
                    .WithObjectSize(content.LongLength)
                    .WithContentType(contentType),
                ct
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MinIO PUT failed for Scribe upload ({FileName}).", fileName);
            return Result.Failure<string>(new Error("CloudStorageClient.Upload", "MinIO PUT failed."));
        }

        await bus.PublishAsync(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = tenantId ?? PlatformTenant.Id,
                FileId = fileId,
                RequestingService = "scribe",
                SourceBucket = options.TempBucket,
                SourceObjectKey = sourceObjectKey,
                ActorId = actorId,
                OwnerType = "Tenant",
                OwnerId = null,
                FolderType = folderType,
                TaxYear = null,
                OriginalName = fileName,
                ContentType = contentType,
                SizeBytes = content.LongLength,
                CorrelationId = correlation.CorrelationId,
            }
        );

        return Result.Success(sourceObjectKey);
    }

    private async Task<Result<string>> AcquireTokenAsync(Guid tenantId, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        return string.IsNullOrEmpty(token)
            ? Result.Failure<string>(new Error("CloudStorageClient.Auth", "No CloudStorage credentials available."))
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
            return Result.Failure<Uri>(new Error("CloudStorageClient.Download", "download-url request failed."));
        }

        var payload = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(Json, ct);
        return payload is null
            ? Result.Failure<Uri>(new Error("CloudStorageClient.Download", "Empty response from download-url."))
            : Result.Success(payload.DownloadUrl);
    }

    private async Task<Result<string>> FetchTextAsync(Uri url, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("MinIO presigned download failed ({Status}).", (int)response.StatusCode);
            return Result.Failure<string>(new Error("CloudStorageClient.Download", "presigned download failed."));
        }

        return Result.Success(await response.Content.ReadAsStringAsync(ct));
    }

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
