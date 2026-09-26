using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Infrastructure.Subscriptions;

namespace TaxVision.PaymentApp.Infrastructure.CloudStorage;

/// <summary>Base URL de CloudStorage. En Docker: http://cloudstorage-api:8080.</summary>
public sealed class CloudStorageClientOptions
{
    public const string SectionName = "CloudStorageClient";

    public string BaseUrl { get; set; } = "http://localhost:5330";
}

/// <summary>
/// Firma la descarga de un recibo contra <c>POST storage/files/{id}/download-url</c>. Molde:
/// <c>Correspondence.CloudStorageClient</c>. PaymentApp ya validó que el pago —y por tanto el recibo— es del
/// tenant que pregunta; acá solo se pide la URL.
/// </summary>
internal sealed class CloudStorageReceiptDownloadUrlClient(
    HttpClient httpClient,
    IPaymentAppServiceTokenAcquirer tokenAcquirer,
    ILogger<CloudStorageReceiptDownloadUrlClient> logger
) : IReceiptDownloadUrlClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<Result<ReceiptDownloadUrl>> GetAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<ReceiptDownloadUrl>(
                new Error("Receipt.Download.Unauthorized", "Could not acquire a service token for the file service.")
            );

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/download-url");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Receipt download url for {FileId} returned {Status}.",
                    fileId,
                    (int)response.StatusCode
                );
                return Result.Failure<ReceiptDownloadUrl>(
                    new Error("Receipt.Download.NotReady", "The receipt is not available for download yet.")
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<DownloadUrlDto>(Json, ct);
            if (payload is null || payload.DownloadUrl is null)
                return Result.Failure<ReceiptDownloadUrl>(
                    new Error("Receipt.Download.NotReady", "The file service returned no download url.")
                );

            return Result.Success(new ReceiptDownloadUrl(payload.DownloadUrl, payload.ExpiresAtUtc));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Receipt download url call threw for file {FileId}.", fileId);
            return Result.Failure<ReceiptDownloadUrl>(
                new Error("Receipt.Download.Unavailable", "The file service is unavailable.")
            );
        }
    }

    private sealed record DownloadUrlDto(Guid FileId, Uri? DownloadUrl, DateTime ExpiresAtUtc);
}
