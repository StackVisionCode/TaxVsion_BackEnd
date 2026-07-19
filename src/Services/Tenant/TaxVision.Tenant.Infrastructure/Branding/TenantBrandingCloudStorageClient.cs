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
using TaxVision.Tenant.Application.Tenants.Abstractions;
using Wolverine;

namespace TaxVision.Tenant.Infrastructure.Branding;

/// <summary>
/// Implementación de <see cref="ITenantBrandingCloudStorageClient"/>. UploadAsync sube directo a
/// MinIO con credenciales propias (IAM scoped a taxvision-temp/tenant-branding/*) y publica
/// SaveFileRequestedIntegrationEvent para que CloudStorage lo registre y escanee de forma
/// asincrona — mismo patrón "Fase D1" ya usado por Signature/Customer, en vez del upload síncrono
/// directo al aggregate (rechazado explícitamente en Tenant_Service_LogoSupport_Plan.md §5.1 por
/// romper la política de desacoplamiento de CloudStorage). GetDownloadUrlAsync/DeleteAsync siguen
/// el flujo HTTP+M2M presignado normal.
/// </summary>
internal sealed class TenantBrandingCloudStorageClient(
    HttpClient httpClient,
    ITenantServiceTokenAcquirer tokenAcquirer,
    IMinioClient minioClient,
    IOptions<TenantMinioOptions> minioOptions,
    IMessageBus bus,
    ICorrelationContext correlation,
    ILogger<TenantBrandingCloudStorageClient> logger
) : ITenantBrandingCloudStorageClient
{
    private const string OwnerTypeTenant = "Tenant";
    private const string FolderTypeBranding = "Branding";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<Guid>> UploadAsync(Guid tenantId, TenantLogoUpload upload, CancellationToken ct = default)
    {
        var fileId = Guid.NewGuid();
        var options = minioOptions.Value;
        var sourceObjectKey = $"{options.SourcePrefix}/{tenantId:N}/{fileId:N}/{upload.FileName}";

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
            logger.LogWarning(ex, "MinIO PUT failed for tenant {TenantId} logo upload.", tenantId);
            return Result.Failure<Guid>(new Error("Tenant.Logo.Storage.Upload", "MinIO PUT failed."));
        }

        await bus.PublishAsync(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = tenantId,
                FileId = fileId,
                RequestingService = "tenant",
                SourceBucket = options.TempBucket,
                SourceObjectKey = sourceObjectKey,
                ActorId = upload.ActorId,
                OwnerType = OwnerTypeTenant,
                OwnerId = tenantId,
                FolderType = FolderTypeBranding,
                TaxYear = null,
                OriginalName = upload.FileName,
                ContentType = upload.ContentType,
                SizeBytes = upload.Content.LongLength,
                CorrelationId = correlation.CorrelationId,
            }
        );

        return Result.Success(fileId);
    }

    public async Task<Result<TenantLogoDownloadUrl>> GetDownloadUrlAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    )
    {
        var tokenResult = await AcquireTokenAsync(tenantId, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<TenantLogoDownloadUrl>(tokenResult.Error);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/download-url");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("CloudStorage download-url call failed ({Status}).", (int)response.StatusCode);
            return Result.Failure<TenantLogoDownloadUrl>(
                new Error("Tenant.Logo.Storage.Download", "download-url request failed.")
            );
        }

        var payload = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(Json, ct);
        return payload is null
            ? Result.Failure<TenantLogoDownloadUrl>(
                new Error("Tenant.Logo.Storage.Download", "Empty response from download-url.")
            )
            : Result.Success(new TenantLogoDownloadUrl(payload.DownloadUrl, payload.ExpiresAtUtc));
    }

    public async Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        var tokenResult = await AcquireTokenAsync(tenantId, ct);
        if (tokenResult.IsFailure)
            return tokenResult;

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"storage/files/{fileId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);

        using var response = await httpClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Result.Success();

        logger.LogWarning(
            "CloudStorage delete call failed ({Status}) for tenant {TenantId} logo file {FileId}.",
            (int)response.StatusCode,
            tenantId,
            fileId
        );
        return Result.Failure(new Error("Tenant.Logo.Storage.Delete", "delete request failed."));
    }

    private async Task<Result<string>> AcquireTokenAsync(Guid tenantId, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        return string.IsNullOrEmpty(token)
            ? Result.Failure<string>(new Error("Tenant.Logo.Storage.Auth", "No CloudStorage credentials available."))
            : Result.Success(token);
    }

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
