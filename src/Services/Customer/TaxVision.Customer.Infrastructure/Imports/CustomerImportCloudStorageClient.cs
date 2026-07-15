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
using TaxVision.Customer.Application.Abstractions;
using Wolverine;

namespace TaxVision.Customer.Infrastructure.Imports;

public sealed class CloudStorageClientOptions
{
    public const string SectionName = "CloudStorageClient";

    /// <summary>Base URL del microservicio CloudStorage (directo o via Gateway). En Docker: http://cloudstorage-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5330";
}

/// <summary>
/// Implementacion de <see cref="ICustomerImportCloudStorageClient"/>. UploadAsync sube
/// directo a MinIO con credenciales propias y publica SaveFileRequestedIntegrationEvent
/// (Fase D); DownloadAsync/DeleteAsync siguen el flujo HTTP+M2M (mismo patron que
/// Signature/Notification).
/// </summary>
internal sealed class CustomerImportCloudStorageClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    IMinioClient minioClient,
    IOptions<CustomerMinioOptions> minioOptions,
    IMessageBus bus,
    ICorrelationContext correlation,
    ILogger<CustomerImportCloudStorageClient> logger
) : ICustomerImportCloudStorageClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result> UploadAsync(
        Guid tenantId,
        Guid fileId,
        byte[] content,
        string fileName,
        string contentType,
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
            logger.LogWarning(ex, "MinIO PUT failed for customer import upload ({FileName}).", fileName);
            return Result.Failure(new Error("Customer.Import.Storage.Upload", "MinIO PUT failed."));
        }

        await bus.PublishAsync(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = tenantId,
                FileId = fileId,
                RequestingService = "customer",
                SourceBucket = options.TempBucket,
                SourceObjectKey = sourceObjectKey,
                ActorId = actorId,
                OwnerType = "Tenant",
                OwnerId = null,
                FolderType = "Imports",
                TaxYear = null,
                OriginalName = fileName,
                ContentType = contentType,
                SizeBytes = content.LongLength,
                CorrelationId = correlation.CorrelationId,
            }
        );

        return Result.Success();
    }

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

    public async Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        var tokenResult = await AcquireTokenAsync(tenantId, ct);
        if (tokenResult.IsFailure)
            return Result.Failure(tokenResult.Error);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"storage/files/{fileId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);

        using var response = await httpClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Result.Success();

        logger.LogWarning(
            "CloudStorage delete call failed ({Status}) for file {FileId}.",
            (int)response.StatusCode,
            fileId
        );
        return Result.Failure(new Error("Customer.Import.Storage.Delete", "delete request failed."));
    }

    // ------------------------------------------------------------------

    private async Task<Result<string>> AcquireTokenAsync(Guid tenantId, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        return string.IsNullOrEmpty(token)
            ? Result.Failure<string>(
                new Error("Customer.Import.Storage.Auth", "No CloudStorage credentials available.")
            )
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
            return Result.Failure<Uri>(new Error("Customer.Import.Storage.Download", "download-url request failed."));
        }
        var payload = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(Json, ct);
        return payload is null
            ? Result.Failure<Uri>(new Error("Customer.Import.Storage.Download", "Empty response from download-url."))
            : Result.Success(payload.DownloadUrl);
    }

    private async Task<Result<byte[]>> FetchBytesAsync(Uri url, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("MinIO presigned download failed ({Status}).", (int)response.StatusCode);
            return Result.Failure<byte[]>(new Error("Customer.Import.Storage.Download", "presigned download failed."));
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return Result.Success(bytes);
    }

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
