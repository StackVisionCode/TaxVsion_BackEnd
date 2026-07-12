using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions.Sealing;

namespace TaxVision.Signature.Infrastructure.Sealing.HttpClients;

/// <summary>
/// Implementación HTTP del <see cref="ISignatureCloudStorageClient"/> siguiendo el mismo
/// flujo presignado que usa Notification (initiate → POST directo a MinIO → complete).
/// Descarga con URL presignada. No accede a MinIO ni a la BD de CloudStorage.
/// Autenticación M2M via <see cref="ISignatureServiceTokenAcquirer"/>.
///
/// <para>Estructurado con métodos privados por fase para mantener SRP.</para>
/// </summary>
internal sealed class SignatureCloudStorageClient(
    HttpClient httpClient,
    ISignatureServiceTokenAcquirer tokenAcquirer,
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
        SignaturePdfUpload upload,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(upload);

        var tokenResult = await AcquireTokenAsync(tenantId, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<Guid>(tokenResult.Error);

        var initResult = await InitiateUploadAsync(upload, tokenResult.Value, ct);
        if (initResult.IsFailure)
            return Result.Failure<Guid>(initResult.Error);

        var putResult = await PostToMinioAsync(initResult.Value, upload, ct);
        if (putResult.IsFailure)
            return Result.Failure<Guid>(putResult.Error);

        var completeResult = await CompleteUploadAsync(initResult.Value.FileId, tokenResult.Value, ct);
        if (completeResult.IsFailure)
            return Result.Failure<Guid>(completeResult.Error);

        return Result.Success(initResult.Value.FileId);
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno con una única responsabilidad
    // ------------------------------------------------------------------

    private async Task<Result<string>> AcquireTokenAsync(Guid tenantId, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        return string.IsNullOrEmpty(token)
            ? Result.Failure<string>(new Error("Signature.Storage.Auth", "No CloudStorage credentials available."))
            : Result.Success(token);
    }

    private async Task<Result<Uri>> FetchDownloadUrlAsync(Guid fileId, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/download-url");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("CloudStorage download-url call failed ({Status}).", (int)response.StatusCode);
            return Result.Failure<Uri>(new Error("Signature.Storage.Download", "download-url request failed."));
        }
        var payload = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(Json, ct);
        return payload is null
            ? Result.Failure<Uri>(new Error("Signature.Storage.Download", "Empty response from download-url."))
            : Result.Success(payload.DownloadUrl);
    }

    private async Task<Result<byte[]>> FetchBytesAsync(Uri url, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("MinIO presigned download failed ({Status}).", (int)response.StatusCode);
            return Result.Failure<byte[]>(new Error("Signature.Storage.Download", "presigned download failed."));
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return Result.Success(bytes);
    }

    private async Task<Result<InitiatedUploadDto>> InitiateUploadAsync(
        SignaturePdfUpload upload,
        string token,
        CancellationToken ct
    )
    {
        var body = new InitiateUploadRequestDto(
            upload.FileName,
            upload.ContentType,
            upload.Content.LongLength,
            upload.OwnerType,
            upload.OwnerId,
            upload.FolderType,
            upload.TaxYear
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "storage/files/uploads")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("CloudStorage initiate call failed ({Status}).", (int)response.StatusCode);
            return Result.Failure<InitiatedUploadDto>(new Error("Signature.Storage.Upload", "initiate failed."));
        }
        var payload = await response.Content.ReadFromJsonAsync<InitiatedUploadDto>(Json, ct);
        return payload is null
            ? Result.Failure<InitiatedUploadDto>(new Error("Signature.Storage.Upload", "empty initiate response."))
            : Result.Success(payload);
    }

    private async Task<Result> PostToMinioAsync(
        InitiatedUploadDto initiated,
        SignaturePdfUpload upload,
        CancellationToken ct
    )
    {
        using var form = new MultipartFormDataContent();
        foreach (var (key, value) in initiated.FormData)
            form.Add(new StringContent(value), key);

        if (!initiated.FormData.Keys.Any(k => k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            form.Add(new StringContent(upload.ContentType), "Content-Type");

        var content = new ByteArrayContent(upload.Content);
        content.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);
        form.Add(content, "file", upload.FileName);

        using var response = await httpClient.PostAsync(initiated.UploadUrl, form, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("MinIO upload failed ({Status}).", (int)response.StatusCode);
            return Result.Failure(new Error("Signature.Storage.Upload", "MinIO PUT failed."));
        }
        return Result.Success();
    }

    private async Task<Result> CompleteUploadAsync(Guid fileId, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId}/complete");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("CloudStorage complete call failed ({Status}).", (int)response.StatusCode);
            return Result.Failure(new Error("Signature.Storage.Upload", "complete failed."));
        }
        return Result.Success();
    }

    // ==================== DTOs privados ====================

    private sealed record InitiateUploadRequestDto(
        string OriginalName,
        string ContentType,
        long SizeBytes,
        string OwnerType,
        Guid? OwnerId,
        string FolderType,
        int? TaxYear
    );

    private sealed record InitiatedUploadDto(
        Guid FileId,
        Uri UploadUrl,
        Dictionary<string, string> FormData,
        DateTime ExpiresAtUtc,
        string Status
    );

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
