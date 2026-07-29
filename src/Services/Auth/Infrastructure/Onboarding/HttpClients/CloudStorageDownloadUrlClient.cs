using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>
/// PayFlow (Fase 11) — respalda el endpoint mediador de descarga del recibo. En cada click, pide a
/// CloudStorage una URL presignada fresca (esas expiran en minutos — ver
/// <c>CloudStorageOptions.PresignedUrlMinutes</c>) en vez de intentar guardar una URL de larga vida
/// que no existe en ningún lado del repo. Mismo patrón de token M2M en-proceso que
/// <see cref="ReceiptDocumentClient"/>: el recibo se guardó bajo <see cref="PlatformTenant.Id"/>
/// (Documents Fase 10), así que ese es el tenant del token — el permiso
/// <c>cloudstorage.file.download</c> viaja embebido porque CloudStorage exige <c>[HasPermission]</c>
/// además de <c>actor_type=Service</c>, y el bypass M2M de <c>ProjectionPermissionsSource</c>
/// (RBAC Fase 7.5.1) lee ese claim directo del JWT en vez de una proyección.
/// </summary>
public sealed class CloudStorageDownloadUrlClient(
    HttpClient httpClient,
    IJwtTokenGenerator tokens,
    ILogger<CloudStorageDownloadUrlClient> logger
) : ICloudStorageDownloadUrlClient
{
    private const string ClientId = "auth-onboarding-receipt-download";
    private const string DownloadPermission = "cloudstorage.file.download";

    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<Result<Uri>> GetDownloadUrlAsync(Guid fileId, CancellationToken ct = default)
    {
        var token = tokens.GenerateScopedServiceToken(
            PlatformTenant.Id,
            ClientId,
            permissions: [DownloadPermission],
            scopes: [],
            audience: "TaxVision.Services",
            lifetimeMinutes: 2
        );

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/download-url");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "CloudStorage download-url request returned {StatusCode} for file {FileId}.",
                    (int)response.StatusCode,
                    fileId
                );
                return Result.Failure<Uri>(
                    new Error(
                        "CloudStorageDownloadUrlClient.UnexpectedStatus",
                        $"CloudStorage returned {(int)response.StatusCode}."
                    )
                );
            }

            var dto = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(ResponseJsonOptions, ct);
            return dto is null
                ? Result.Failure<Uri>(
                    new Error("CloudStorageDownloadUrlClient.EmptyResponse", "CloudStorage returned an empty response.")
                )
                : Result.Success(dto.DownloadUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "CloudStorage download-url request failed for file {FileId}.", fileId);
            return Result.Failure<Uri>(
                new Error("CloudStorageDownloadUrlClient.RequestFailed", "Could not reach CloudStorage.")
            );
        }
    }

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
