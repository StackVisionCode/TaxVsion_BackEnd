using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Infrastructure.Customers;

namespace TaxVision.Correspondence.Infrastructure.CloudStorage;

/// <summary>
/// Implementación de <see cref="ICloudStorageClient"/> contra CloudStorage.Api — <c>POST /storage/files/{id}/download-url</c>
/// (Fase 8) y <c>GET /storage/files/{id}</c> (Fase 12, verificación best-effort) — mismo patrón exacto que
/// <c>CloudStorageOutboundAttachmentFetcher.FetchDownloadUrlAsync</c> (Postmaster) y
/// <c>SignatureCloudStorageClient.FetchDownloadUrlAsync</c>: token M2M vía
/// <see cref="ICorrespondenceServiceTokenAcquirer"/>, sin retry (a diferencia de <see cref="Connectors.ConnectorsClient"/>,
/// estas llamadas las dispara un request de usuario final, no vale la pena reintentar en el mismo request).
/// </summary>
internal sealed class CloudStorageClient(
    HttpClient httpClient,
    ICorrespondenceServiceTokenAcquirer tokenAcquirer,
    ILogger<CloudStorageClient> logger
) : ICloudStorageClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<CloudStorageDownloadUrl>> GetDownloadUrlAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<CloudStorageDownloadUrl>(
                new Error(
                    "CloudStorageClient.ServiceAuthUnavailable",
                    "Could not acquire a service token to call CloudStorage."
                )
            );

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/download-url");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CloudStorage download-url call failed ({Status}).", (int)response.StatusCode);
                return Result.Failure<CloudStorageDownloadUrl>(
                    new Error(
                        "CloudStorageClient.UnexpectedStatus",
                        $"CloudStorage returned HTTP {(int)response.StatusCode}."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(Json, ct);
            return payload is null
                ? Result.Failure<CloudStorageDownloadUrl>(
                    new Error("CloudStorageClient.EmptyResponse", "Empty response from download-url.")
                )
                : Result.Success(new CloudStorageDownloadUrl(payload.DownloadUrl, payload.ExpiresAtUtc));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "CloudStorage download-url call threw for file {FileId}.", fileId);
            return Result.Failure<CloudStorageDownloadUrl>(new Error("CloudStorageClient.RequestFailed", ex.Message));
        }
    }

    /// <summary>
    /// Fase 12 — contra <c>GET /storage/files/{id}</c>. A diferencia de <see cref="GetDownloadUrlAsync"/>,
    /// el caller (<c>AttachFileToDraftHandler</c>) nunca hard-falla por lo que esta llamada devuelva —
    /// por eso acá alcanza con la misma forma de <see cref="Result{T}"/> que el resto del cliente, sin
    /// distinguir 404 de timeout: cualquier fallo es igual de "no pude confirmar" para el caller.
    /// </summary>
    public async Task<Result<CloudStorageFileMetadata>> GetFileMetadataAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<CloudStorageFileMetadata>(
                new Error(
                    "CloudStorageClient.ServiceAuthUnavailable",
                    "Could not acquire a service token to call CloudStorage."
                )
            );

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"storage/files/{fileId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CloudStorage file lookup call failed ({Status}).", (int)response.StatusCode);
                return Result.Failure<CloudStorageFileMetadata>(
                    new Error(
                        "CloudStorageClient.UnexpectedStatus",
                        $"CloudStorage returned HTTP {(int)response.StatusCode}."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<FileMetadataResponseDto>(Json, ct);
            return payload is null
                ? Result.Failure<CloudStorageFileMetadata>(
                    new Error("CloudStorageClient.EmptyResponse", "Empty response from file lookup.")
                )
                : Result.Success(
                    new CloudStorageFileMetadata(payload.Id, payload.DeclaredContentType, payload.SizeBytes)
                );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "CloudStorage file lookup call threw for file {FileId}.", fileId);
            return Result.Failure<CloudStorageFileMetadata>(new Error("CloudStorageClient.RequestFailed", ex.Message));
        }
    }

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);

    private sealed record FileMetadataResponseDto(Guid Id, string DeclaredContentType, long SizeBytes);
}
