using System.Net;
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
/// Descarga bytes de <see cref="OutboundAttachmentRef"/> desde CloudStorage vía el mismo flujo
/// HTTP+M2M presignado (initiate download-url → GET) que <see cref="CloudStorageInlineAssetFetcher"/>
/// — a diferencia de ese, sin cap agregado: el límite de logos CID (5MB) no aplica a adjuntos de
/// correspondencia, el cap real lo aplica el proveedor resuelto recién al enviar (D3 Compose §11.3/§12).
/// </summary>
public sealed class CloudStorageOutboundAttachmentFetcher(
    HttpClient httpClient,
    IPostmasterServiceTokenAcquirer tokenAcquirer,
    ILogger<CloudStorageOutboundAttachmentFetcher> logger,
    TimeSpan? scanRetryDelay = null
) : IOutboundAttachmentFetcher
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // El adjunto se sube justo antes de enviar y CloudStorage lo escanea async: hasta que el scan lo
    // marca Available, download-url responde 403 (NotAvailable). El adjunto NO debe salir sin escanear,
    // así que se espera con reintentos acotados la carrera upload→scan en vez de fallar el envío.
    private const int DownloadUrlMaxAttempts = 6;
    private readonly TimeSpan _scanRetryDelay = scanRetryDelay ?? TimeSpan.FromMilliseconds(500);

    public async Task<Result<IReadOnlyList<OutboundAttachmentBytes>>> FetchAllAsync(
        Guid tenantId,
        IReadOnlyList<OutboundAttachmentRef> attachments,
        CancellationToken ct
    )
    {
        if (attachments.Count == 0)
            return Result.Success<IReadOnlyList<OutboundAttachmentBytes>>([]);

        var tokenResult = await AcquireTokenAsync(tenantId, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<IReadOnlyList<OutboundAttachmentBytes>>(tokenResult.Error);

        var results = new List<OutboundAttachmentBytes>(attachments.Count);
        foreach (var attachment in attachments)
        {
            var fetched = await FetchOneAsync(attachment, tokenResult.Value, ct);
            if (fetched.IsFailure)
                return Result.Failure<IReadOnlyList<OutboundAttachmentBytes>>(fetched.Error);

            results.Add(fetched.Value);
        }

        return Result.Success<IReadOnlyList<OutboundAttachmentBytes>>(results);
    }

    private async Task<Result<OutboundAttachmentBytes>> FetchOneAsync(
        OutboundAttachmentRef attachment,
        string token,
        CancellationToken ct
    )
    {
        var urlResult = await FetchDownloadUrlAsync(attachment.CloudStorageFileId, token, ct);
        if (urlResult.IsFailure)
            return Result.Failure<OutboundAttachmentBytes>(urlResult.Error);

        var bytesResult = await FetchBytesAsync(urlResult.Value, ct);
        if (bytesResult.IsFailure)
            return Result.Failure<OutboundAttachmentBytes>(bytesResult.Error);

        return Result.Success(
            new OutboundAttachmentBytes(attachment.Filename, attachment.ContentType, bytesResult.Value)
        );
    }

    private async Task<Result<string>> AcquireTokenAsync(Guid tenantId, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        return string.IsNullOrEmpty(token)
            ? Result.Failure<string>(
                new Error("OutboundAttachmentFetcher.Auth", "No CloudStorage credentials available.")
            )
            : Result.Success(token);
    }

    /// <summary>
    /// Fase 13 (hardening) — <see cref="HttpRequestException"/>/<see cref="TaskCanceledException"/> (esta
    /// última cubre tanto cancelación explícita como el timeout de 30s del <c>HttpClient</c>, ver
    /// DependencyInjection) ya no suben sin atrapar: se traducen al mismo <see cref="Result"/> failure
    /// que un status code no-success, mismo criterio que <c>ConnectorsSendClient.SendAsync</c>.
    /// </summary>
    private async Task<Result<Uri>> FetchDownloadUrlAsync(Guid fileId, string token, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= DownloadUrlMaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/download-url");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await httpClient.SendAsync(request, ct);

                // 403 = archivo aún no Available (escaneo en curso justo tras subirlo): esperar y reintentar.
                if (response.StatusCode == HttpStatusCode.Forbidden && attempt < DownloadUrlMaxAttempts)
                {
                    await Task.Delay(_scanRetryDelay, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "CloudStorage download-url call failed ({Status}) for file {FileId} after {Attempts} attempt(s).",
                        (int)response.StatusCode,
                        fileId,
                        attempt
                    );
                    return Result.Failure<Uri>(
                        new Error("OutboundAttachmentFetcher.Download", "download-url request failed.")
                    );
                }

                var payload = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(Json, ct);
                return payload is null
                    ? Result.Failure<Uri>(
                        new Error("OutboundAttachmentFetcher.Download", "Empty response from download-url.")
                    )
                    : Result.Success(payload.DownloadUrl);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "CloudStorage download-url call errored for file {FileId}.", fileId);
                return Result.Failure<Uri>(
                    new Error("OutboundAttachmentFetcher.Download", "download-url request failed.")
                );
            }
        }

        // Presupuesto agotado: el archivo sigue sin pasar el escaneo.
        logger.LogWarning(
            "CloudStorage download-url still NotAvailable for file {FileId} after {Attempts} attempts.",
            fileId,
            DownloadUrlMaxAttempts
        );
        return Result.Failure<Uri>(
            new Error("OutboundAttachmentFetcher.Download", "Attachment is still being scanned; try again in a moment.")
        );
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
                    new Error("OutboundAttachmentFetcher.Download", "presigned download failed.")
                );
            }

            return Result.Success(await response.Content.ReadAsByteArrayAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "MinIO presigned download errored for {Url}.", url);
            return Result.Failure<byte[]>(
                new Error("OutboundAttachmentFetcher.Download", "presigned download failed.")
            );
        }
    }

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
