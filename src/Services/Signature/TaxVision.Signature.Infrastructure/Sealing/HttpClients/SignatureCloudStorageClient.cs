using System.Net;
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
        SignatureFileUpload upload,
        CancellationToken ct = default
    )
    {
        var staged = await StageAsync(tenantId, upload, ct);
        if (staged.IsFailure)
            return Result.Failure<Guid>(staged.Error);

        await bus.PublishAsync(staged.Value);
        return Result.Success(staged.Value.FileId);
    }

    public async Task<Result<SaveFileRequestedIntegrationEvent>> StageAsync(
        Guid tenantId,
        SignatureFileUpload upload,
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
            return Result.Failure<SaveFileRequestedIntegrationEvent>(
                new Error(SignatureStorage.TransientCode, "MinIO PUT failed.")
            );
        }

        return Result.Success(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = tenantId,
                FileId = fileId,
                RequestingService = SignatureStorage.RequestingService,
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
    }

    public async Task<Result<Guid>> CopyToCustomerAsync(
        Guid tenantId,
        Guid sourceFileId,
        Guid customerId,
        int taxYear,
        CancellationToken ct = default
    )
    {
        var tokenResult = await AcquireTokenAsync(tenantId, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<Guid>(tokenResult.Error);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{sourceFileId}/copies")
        {
            Content = JsonContent.Create(
                new CopyFileRequestDto("Customer", customerId, "Signatures", taxYear),
                options: Json
            ),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "CloudStorage copy of {FileId} failed ({Status}).",
                sourceFileId,
                (int)response.StatusCode
            );
            return Result.Failure<Guid>(new Error("Signature.Storage.Copy", "CloudStorage copy request failed."));
        }
        var payload = await response.Content.ReadFromJsonAsync<CopiedFileResponseDto>(Json, ct);
        return payload is null
            ? Result.Failure<Guid>(new Error("Signature.Storage.Copy", "Empty response from CloudStorage copy."))
            : Result.Success(payload.FileId);
    }

    public async Task<Result<Uri>> GetDownloadUrlAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        var tokenResult = await AcquireTokenAsync(tenantId, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<Uri>(tokenResult.Error);

        return await FetchDownloadUrlAsync(fileId, tokenResult.Value, ct);
    }

    public async Task<Result<SignatureFileMetadata>> GetFileAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    )
    {
        // Resiliencia: la proyección local se alimenta por bus best-effort. Cuando falta o está
        // stale (Deleted mientras el file sigue vivo en CloudStorage, FileAvailable perdido por un
        // restart), este GET consulta la fuente autoritaria para desbloquear al preparador.
        var tokenResult = await AcquireTokenAsync(tenantId, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<SignatureFileMetadata>(tokenResult.Error);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"storage/files/{fileId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return Result.Failure<SignatureFileMetadata>(
                    new Error(SignatureStorage.FileNotFoundCode, "File not found in CloudStorage.")
                );
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CloudStorage file lookup failed ({Status}).", (int)response.StatusCode);
                return Result.Failure<SignatureFileMetadata>(
                    new Error("Signature.Storage.FileLookup", $"CloudStorage returned HTTP {(int)response.StatusCode}.")
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<FileLookupResponseDto>(Json, ct);
            return payload is null
                ? Result.Failure<SignatureFileMetadata>(
                    new Error("Signature.Storage.FileLookup", "Empty response from CloudStorage.")
                )
                : Result.Success(
                    new SignatureFileMetadata(
                        payload.Id,
                        payload.Status,
                        payload.ChecksumSha256,
                        payload.DetectedContentType ?? payload.DeclaredContentType,
                        payload.SizeBytes,
                        null,
                        payload.OwnerType,
                        payload.FolderType
                    )
                );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // El caller tolera el fallo: nunca bloqueamos la creación por un lookup caído.
            logger.LogWarning(ex, "CloudStorage file lookup threw.");
            return Result.Failure<SignatureFileMetadata>(
                new Error("Signature.Storage.FileLookup", "CloudStorage lookup unavailable.")
            );
        }
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno con una única responsabilidad
    // ------------------------------------------------------------------

    private async Task<Result<string>> AcquireTokenAsync(Guid tenantId, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        return string.IsNullOrEmpty(token)
            ? Result.Failure<string>(
                new Error(SignatureStorage.TransientCode, "No CloudStorage credentials available.")
            )
            : Result.Success(token);
    }

    private async Task<Result<Uri>> FetchDownloadUrlAsync(Guid fileId, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/download-url");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            logger.LogWarning(ex, "CloudStorage download-url call threw.");
            return Result.Failure<Uri>(new Error(SignatureStorage.TransientCode, "download-url request failed."));
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("CloudStorage download-url call failed ({Status}).", (int)response.StatusCode);
            return Result.Failure<Uri>(
                new Error(
                    ErrorCodeFor(response.StatusCode, "Signature.Storage.Download"),
                    "download-url request failed."
                )
            );
        }
        var payload = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(Json, ct);
        return payload is null
            ? Result.Failure<Uri>(new Error("Signature.Storage.Download", "Empty response from download-url."))
            : Result.Success(payload.DownloadUrl);
    }

    private async Task<Result<byte[]>> FetchBytesAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var response = await httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("MinIO presigned download failed ({Status}).", (int)response.StatusCode);
                return Result.Failure<byte[]>(
                    new Error(
                        ErrorCodeFor(response.StatusCode, "Signature.Storage.Download"),
                        "presigned download failed."
                    )
                );
            }
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return Result.Success(bytes);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            logger.LogWarning(ex, "MinIO presigned download threw.");
            return Result.Failure<byte[]>(new Error(SignatureStorage.TransientCode, "presigned download failed."));
        }
    }

    // 5xx, 408 y 429 pueden mejorar reintentando; el resto de 4xx no.
    private static string ErrorCodeFor(HttpStatusCode status, string permanentCode) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            ? SignatureStorage.TransientCode
            : permanentCode;

    // Red caída o timeout del HttpClient; una cancelación pedida por el host no es un fallo del servicio.
    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    // ==================== DTOs privados ====================

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);

    // Enums de CloudStorage serializados como string (JsonStringEnumConverter en su API).
    private sealed record CopyFileRequestDto(string OwnerType, Guid OwnerId, string FolderType, int TaxYear);

    private sealed record CopiedFileResponseDto(Guid FileId);

    // Subset de FileResponse (CloudStorage FileContracts): solo lo que GetFileAsync necesita.
    private sealed record FileLookupResponseDto(
        Guid Id,
        string Status,
        string? ChecksumSha256,
        string? DeclaredContentType,
        string? DetectedContentType,
        long SizeBytes,
        // P-18: owner/carpeta (enums serializados como string por el API de CloudStorage).
        string? OwnerType = null,
        string? FolderType = null
    );
}
