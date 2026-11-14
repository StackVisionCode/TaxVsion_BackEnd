using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Options;
using TaxVision.FinancialInsights.Application.Imports;

namespace TaxVision.FinancialInsights.Infrastructure.Processing;

public sealed record StatementFile(byte[] Bytes, string MediaType);

public sealed class CloudStorageStatementSource(HttpClient http, IServiceTokenAcquirer tokens,
    IOptions<FinancialProcessingOptions> options)
{
    private const int MaxBytes = 25 * 1024 * 1024;

    public async Task<StatementFile> DownloadAsync(Guid tenantId, Guid customerId, Guid fileId, CancellationToken ct)
    {
        var token = await tokens.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(token))
            throw new DocumentProcessingException("StorageAuth", "CloudStorage service token is unavailable.");
        using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, $"storage/files/{fileId:D}");
        metadataRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var metadataResponse = await http.SendAsync(metadataRequest, ct);
        if (!metadataResponse.IsSuccessStatusCode)
            throw new DocumentProcessingException("StorageMetadata", "File metadata is unavailable.");
        using var metadata = await JsonDocument.ParseAsync(await metadataResponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var file = metadata.RootElement;
        var owner = ReadEnum(file, "ownerType", "Tenant", "Customer", "User", "Signature", "Invoice", "Communication");
        var status = ReadEnum(file, "status", "PendingUpload", "PendingScan", "Scanning", "Available",
            "Infected", "ScanFailed", "SoftDeleted", "BlockedByPolicy", "PendingReview");
        var mediaType = file.TryGetProperty("detectedContentType", out var content) && content.ValueKind == JsonValueKind.String
            ? content.GetString() : null;
        var size = file.TryGetProperty("sizeBytes", out var length) && length.TryGetInt64(out var value) ? value : 0;
        var ownerId = file.TryGetProperty("ownerId", out var id) && id.ValueKind == JsonValueKind.String
            && id.TryGetGuid(out var parsed) ? parsed : Guid.Empty;
        if (!file.TryGetProperty("id", out var metadataId) || !metadataId.TryGetGuid(out var receivedId)
            || receivedId != fileId || !ImportFilePolicy.CanRegister(owner, ownerId, customerId, status, mediaType, size))
            throw new DocumentProcessingException("StorageEligibility", "File is not an available statement for this customer.");

        using var urlRequest = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId:D}/download-url");
        urlRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var urlResponse = await http.SendAsync(urlRequest, ct);
        if (!urlResponse.IsSuccessStatusCode)
            throw new DocumentProcessingException("StorageDownloadUrl", "CloudStorage did not issue a download URL.");
        var signed = await urlResponse.Content.ReadFromJsonAsync<SignedUrl>(cancellationToken: ct);
        if (signed is null || signed.FileId != fileId || signed.ExpiresAtUtc <= DateTime.UtcNow
            || signed.DownloadUrl.Scheme is not ("http" or "https")
            || (signed.DownloadUrl.Scheme == "http" && signed.DownloadUrl.Port != 9000)
            || (signed.DownloadUrl.Scheme == "https" && signed.DownloadUrl.Port != 443)
            || !string.IsNullOrEmpty(signed.DownloadUrl.UserInfo)
            || !options.Value.AllowedDownloadHosts.Contains(signed.DownloadUrl.Host, StringComparer.OrdinalIgnoreCase))
            throw new DocumentProcessingException("StorageDownloadUrl", "CloudStorage returned an untrusted download URL.");

        // Never attach the service token to the presigned request.
        using var download = await http.GetAsync(signed.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!download.IsSuccessStatusCode || download.Content.Headers.ContentLength is > MaxBytes)
            throw new DocumentProcessingException("StorageDownload", "Statement download failed or exceeded size limit.");
        await using var input = await download.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(chunk, ct);
            if (read == 0) break;
            if (buffer.Length + read > MaxBytes)
                throw new DocumentProcessingException("StorageDownload", "Statement download exceeded size limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        return new StatementFile(buffer.ToArray(), mediaType!);
    }

    private static string? ReadEnum(JsonElement parent, string name, params string[] values)
    {
        if (!parent.TryGetProperty(name, out var element)) return null;
        if (element.ValueKind == JsonValueKind.String) return element.GetString();
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var index) && index >= 0 && index < values.Length)
            return values[index];
        return null;
    }

    private sealed record SignedUrl(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
