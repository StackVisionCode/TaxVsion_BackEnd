using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;

namespace TaxVision.Documents.Infrastructure.Branding;

public sealed class CloudStorageClientOptions
{
    public const string SectionName = "CloudStorageClient";

    /// <summary>Base URL del servicio CloudStorage. En Docker: http://cloudstorage-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5170";
}

/// <summary>
/// Resuelve el logo del tenant (proyección local <c>TenantLogoRef</c> → bytes de CloudStorage) como un
/// <c>data:</c> URI para embeber en el PDF. Usa el flujo M2M presignado
/// (<c>POST storage/files/{id}/download-url</c> → GET), el mismo que ya usan Scribe/Postmaster/Signature.
/// Best-effort: cualquier fallo (sin logo, sin credenciales M2M, sin permiso, error de red) devuelve
/// <c>null</c> y el PDF se genera sin logo (comportamiento actual) — nunca lanza.
/// </summary>
public sealed class CloudStorageTenantLogoResolver(
    HttpClient httpClient,
    ITenantLogoRefRepository repository,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<CloudStorageTenantLogoResolver> logger
) : ITenantLogoResolver
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<string?> ResolveLogoDataUriAsync(Guid tenantId, CancellationToken ct = default)
    {
        var logoRef = await repository.GetByTenantAsync(tenantId, ct);
        if (logoRef is null || !logoRef.HasLogo)
            return null;

        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning(
                "No M2M token to fetch tenant logo for {TenantId}; PDF will render without logo.",
                tenantId
            );
            return null;
        }

        try
        {
            var url = await FetchDownloadUrlAsync(logoRef.LogoFileId!.Value, token, ct);
            if (url is null)
                return null;

            using var fileResponse = await httpClient.GetAsync(url, ct);
            if (!fileResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Tenant logo presigned download failed ({Status}) for {TenantId}.",
                    (int)fileResponse.StatusCode,
                    tenantId
                );
                return null;
            }

            var bytes = await fileResponse.Content.ReadAsByteArrayAsync(ct);
            if (bytes.LongLength == 0 || bytes.LongLength > MaxBytes)
                return null;

            return $"data:{logoRef.LogoContentType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(
                ex,
                "Could not resolve tenant logo for {TenantId}; PDF will render without logo.",
                tenantId
            );
            return null;
        }
    }

    private async Task<Uri?> FetchDownloadUrlAsync(Guid fileId, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/download-url");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "CloudStorage download-url call failed ({Status}) for file {FileId}.",
                (int)response.StatusCode,
                fileId
            );
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(Json, ct);
        return payload?.DownloadUrl;
    }

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
