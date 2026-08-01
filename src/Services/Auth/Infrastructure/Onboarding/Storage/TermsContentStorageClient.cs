using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Infrastructure.Onboarding.Security;
using Wolverine;

namespace TaxVision.Auth.Infrastructure.Onboarding.Storage;

/// <summary>
/// Auditoría (gap MinIO/legal-docs) — combina las dos mitades del patrón D0/D1, igual que
/// Scribe's <c>CloudStorageClient</c>: sube directo a MinIO con IAM propia de Auth (nunca las
/// credenciales root de CloudStorage) y publica <see cref="SaveFileRequestedIntegrationEvent"/>
/// para que CloudStorage lo registre/escanee; y lee vía HTTP+M2M (mint de JWT en proceso, mismo
/// patrón que <c>CloudStorageDownloadUrlClient</c>, pero con su propio <c>ClientId</c> — cada
/// cliente M2M de Onboarding usa parámetros fijos por <c>ClientId</c>, ver
/// <c>OnboardingServiceTokenCache</c> Auditoría F27).
/// <para>
/// El documento vive bajo <see cref="PlatformTenant.Id"/> con <c>OwnerType=Tenant</c>/<c>OwnerId=null</c>
/// y <c>FolderType=Templates</c> (política existente: 5 MB, permite .html/text-html) — es un recurso
/// de plataforma, no de un tenant real, igual que los templates System de Scribe.
/// </para>
/// </summary>
public sealed class TermsContentStorageClient(
    IMinioClient minioClient,
    IOptions<AuthMinioOptions> minioOptions,
    IMessageBus bus,
    ICorrelationContext correlation,
    HttpClient httpClient,
    OnboardingServiceTokenCache tokenCache,
    ILogger<TermsContentStorageClient> logger
) : ITermsContentStorageClient
{
    private const string ClientId = "auth-terms-content-download";
    private const string DownloadPermission = "cloudstorage.file.download";

    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<Result> UploadAsync(
        Guid fileId,
        byte[] content,
        string fileName,
        string contentType,
        Guid actorId,
        CancellationToken ct = default
    )
    {
        var opt = minioOptions.Value;
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
            logger.LogWarning(ex, "MinIO PUT failed for terms content upload {FileName} ({FileId}).", fileName, fileId);
            return Result.Failure(new Error("TermsContentStorageClient.Upload", "MinIO PUT failed."));
        }

        await bus.PublishAsync(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                CorrelationId = correlation.CorrelationId,
                FileId = fileId,
                RequestingService = "auth",
                SourceBucket = opt.TempBucket,
                SourceObjectKey = sourceObjectKey,
                ActorId = actorId,
                OwnerType = "Tenant",
                OwnerId = null,
                FolderType = "Templates",
                TaxYear = null,
                OriginalName = fileName,
                ContentType = contentType,
                SizeBytes = content.LongLength,
            }
        );

        return Result.Success();
    }

    public async Task<Result<string>> DownloadTextAsync(Guid fileId, CancellationToken ct = default)
    {
        var token = await tokenCache.GetOrCreateAsync(
            PlatformTenant.Id,
            ClientId,
            permissions: [DownloadPermission],
            scopes: [],
            audience: "TaxVision.Services",
            lifetimeMinutes: 2,
            ct
        );

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/download-url");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "CloudStorage download-url request returned {StatusCode} for terms content file {FileId}.",
                    (int)response.StatusCode,
                    fileId
                );
                return Result.Failure<string>(
                    new Error(
                        "TermsContentStorageClient.UnexpectedStatus",
                        $"CloudStorage returned {(int)response.StatusCode}."
                    )
                );
            }

            var dto = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(ResponseJsonOptions, ct);
            if (dto is null)
                return Result.Failure<string>(
                    new Error("TermsContentStorageClient.EmptyResponse", "CloudStorage returned an empty response.")
                );

            using var contentResponse = await httpClient.GetAsync(dto.DownloadUrl, ct);
            if (!contentResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "MinIO presigned download failed ({Status}) for terms content file {FileId}.",
                    (int)contentResponse.StatusCode,
                    fileId
                );
                return Result.Failure<string>(
                    new Error("TermsContentStorageClient.PresignedDownloadFailed", "Presigned download failed.")
                );
            }

            return Result.Success(await contentResponse.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Terms content download failed for file {FileId}.", fileId);
            return Result.Failure<string>(
                new Error("TermsContentStorageClient.RequestFailed", "Could not reach CloudStorage.")
            );
        }
    }

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
