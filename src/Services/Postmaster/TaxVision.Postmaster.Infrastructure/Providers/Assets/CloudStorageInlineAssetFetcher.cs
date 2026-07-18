using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.Providers.Assets;

/// <summary>
/// Descarga bytes de <see cref="InlineAsset"/> desde CloudStorage vía el flujo HTTP+M2M presignado
/// (initiate download-url → GET), el mismo patrón ya usado por Signature para downloads síncronos
/// (ver project_cloudstorage_hardening_plan.md — la Fase D solo desacopló el path de upload).
/// Estructurado con métodos privados por fase para mantener SRP.
/// </summary>
public sealed class CloudStorageInlineAssetFetcher(
    HttpClient httpClient,
    IPostmasterServiceTokenAcquirer tokenAcquirer,
    ILogger<CloudStorageInlineAssetFetcher> logger
) : IInlineAssetFetcher
{
    private const long MaxTotalBytes = 5 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<IReadOnlyList<InlineAssetBytes>>> FetchAllAsync(
        Guid tenantId,
        IReadOnlyList<InlineAsset> inlineAssets,
        CancellationToken ct
    )
    {
        var totalSizeCheck = ValidateTotalSize(inlineAssets);
        if (totalSizeCheck.IsFailure)
            return Result.Failure<IReadOnlyList<InlineAssetBytes>>(totalSizeCheck.Error);

        var tokenResult = await AcquireTokenAsync(tenantId, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<IReadOnlyList<InlineAssetBytes>>(tokenResult.Error);

        var results = new List<InlineAssetBytes>(inlineAssets.Count);
        foreach (var asset in inlineAssets)
        {
            var fetched = await FetchOneAsync(asset, tokenResult.Value, ct);
            if (fetched.IsFailure)
                return Result.Failure<IReadOnlyList<InlineAssetBytes>>(fetched.Error);

            results.Add(fetched.Value);
        }

        return Result.Success<IReadOnlyList<InlineAssetBytes>>(results);
    }

    private static Result ValidateTotalSize(IReadOnlyList<InlineAsset> inlineAssets)
    {
        long total = 0;
        foreach (var asset in inlineAssets)
            total += asset.SizeBytes;

        return total > MaxTotalBytes
            ? Result.Failure(
                new Error(
                    "InlineAssetFetcher.TotalSizeExceeded",
                    $"Total inline assets size {total} exceeds the {MaxTotalBytes} bytes limit."
                )
            )
            : Result.Success();
    }

    private async Task<Result<InlineAssetBytes>> FetchOneAsync(InlineAsset asset, string token, CancellationToken ct)
    {
        var urlResult = await FetchDownloadUrlAsync(asset.CloudStorageFileId, token, ct);
        if (urlResult.IsFailure)
            return Result.Failure<InlineAssetBytes>(urlResult.Error);

        var bytesResult = await FetchBytesAsync(urlResult.Value, ct);
        if (bytesResult.IsFailure)
            return Result.Failure<InlineAssetBytes>(bytesResult.Error);

        var fileName = $"{asset.ContentId}{ExtensionForContentType(asset.ContentType)}";
        return Result.Success(new InlineAssetBytes(asset.ContentId, bytesResult.Value, asset.ContentType, fileName));
    }

    private async Task<Result<string>> AcquireTokenAsync(Guid tenantId, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        return string.IsNullOrEmpty(token)
            ? Result.Failure<string>(new Error("InlineAssetFetcher.Auth", "No CloudStorage credentials available."))
            : Result.Success(token);
    }

    /// <summary>
    /// Fase 13 (hardening) — <see cref="HttpRequestException"/>/<see cref="TaskCanceledException"/> (esta
    /// última cubre tanto cancelación explícita como el timeout de 30s del <c>HttpClient</c>, ver
    /// DependencyInjection) ya no suben sin atrapar: se traducen al mismo <see cref="Result"/> failure
    /// que un status code no-success, mismo criterio que <c>ConnectorsSendClient.SendAsync</c> y
    /// <see cref="CloudStorageOutboundAttachmentFetcher"/>.
    /// </summary>
    private async Task<Result<Uri>> FetchDownloadUrlAsync(Guid fileId, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/download-url");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CloudStorage download-url call failed ({Status}).", (int)response.StatusCode);
                return Result.Failure<Uri>(new Error("InlineAssetFetcher.Download", "download-url request failed."));
            }

            var payload = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(Json, ct);
            return payload is null
                ? Result.Failure<Uri>(new Error("InlineAssetFetcher.Download", "Empty response from download-url."))
                : Result.Success(payload.DownloadUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "CloudStorage download-url call errored for file {FileId}.", fileId);
            return Result.Failure<Uri>(new Error("InlineAssetFetcher.Download", "download-url request failed."));
        }
    }

    private async Task<Result<byte[]>> FetchBytesAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var response = await httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("MinIO presigned download failed ({Status}).", (int)response.StatusCode);
                return Result.Failure<byte[]>(new Error("InlineAssetFetcher.Download", "presigned download failed."));
            }

            return Result.Success(await response.Content.ReadAsByteArrayAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "MinIO presigned download errored for {Url}.", url);
            return Result.Failure<byte[]>(new Error("InlineAssetFetcher.Download", "presigned download failed."));
        }
    }

    private static string ExtensionForContentType(string contentType) =>
        contentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            _ => string.Empty,
        };

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
