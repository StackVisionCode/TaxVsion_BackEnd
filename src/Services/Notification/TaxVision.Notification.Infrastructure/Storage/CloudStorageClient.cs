using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Storage;

/// <summary>
/// Cliente HTTP del microservicio CloudStorage. Implementa el flujo presignado de tres pasos
/// (initiate → POST directo a MinIO → complete) y la descarga por URL presignada. Reenvía el bearer
/// token provisto por <see cref="ICloudStorageTokenProvider"/>; nunca accede a MinIO/BD directamente.
/// </summary>
public sealed class CloudStorageClient(
    HttpClient httpClient,
    ICloudStorageTokenProvider tokenProvider,
    ILogger<CloudStorageClient> logger
) : ICloudStorageClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<Guid>> UploadAsync(
        CloudStorageUpload upload,
        Guid? tenantId = null,
        CancellationToken ct = default
    )
    {
        var token = await tokenProvider.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<Guid>(new Error("Email.StorageAuth", "No CloudStorage credentials available."));

        // 1) Initiate: reserva y URL presignada de subida.
        var initReq = new InitiateUploadRequestDto(
            upload.OriginalName,
            upload.ContentType,
            upload.Content.LongLength,
            upload.OwnerType,
            upload.OwnerId,
            upload.FolderType,
            upload.TaxYear
        );

        var initResult = await SendJsonAsync<InitiatedUploadResponseDto>(
            HttpMethod.Post,
            "storage/files/uploads",
            initReq,
            token,
            ct
        );
        if (initResult.IsFailure)
            return Result.Failure<Guid>(initResult.Error);

        var initiated = initResult.Value;

        // 2) POST directo del binario a MinIO con la form-data presignada (el campo "file" va al final).
        using var form = new MultipartFormDataContent();
        foreach (var (key, value) in initiated.FormData)
            form.Add(new StringContent(value), key);

        // La política POST de MinIO incluye una condición sobre el campo de formulario
        // Content-Type. La cabecera de la parte "file" no satisface por sí sola esa condición.
        if (!initiated.FormData.Keys.Any(key => key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            form.Add(new StringContent(upload.ContentType), "Content-Type");

        var fileContent = new ByteArrayContent(upload.Content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);
        form.Add(fileContent, "file", upload.OriginalName);

        using var minioResponse = await httpClient.PostAsync(initiated.UploadUrl, form, ct);
        if (!minioResponse.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "CloudStorage MinIO upload failed ({Status}) for file {FileId}.",
                (int)minioResponse.StatusCode,
                initiated.FileId
            );
            return Result.Failure<Guid>(new Error("Email.StorageUpload", "Object upload to storage failed."));
        }

        // 3) Complete: dispara el escaneo antivirus. El archivo queda disponible tras escanear.
        var completeResult = await SendAsync($"storage/files/{initiated.FileId}/complete", token, ct);
        if (completeResult.IsFailure)
            return Result.Failure<Guid>(completeResult.Error);

        return Result.Success(initiated.FileId);
    }

    public async Task<Result<Uri>> GetDownloadUrlAsync(
        Guid fileId,
        Guid? tenantId = null,
        CancellationToken ct = default
    )
    {
        var token = await tokenProvider.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<Uri>(new Error("Email.StorageAuth", "No CloudStorage credentials available."));

        var result = await SendJsonAsync<DownloadUrlResponseDto>(
            HttpMethod.Post,
            $"storage/files/{fileId}/download-url",
            null,
            token,
            ct
        );
        return result.IsFailure ? Result.Failure<Uri>(result.Error) : Result.Success(result.Value.DownloadUrl);
    }

    public async Task<Result<string>> DownloadTextAsync(
        Guid fileId,
        Guid? tenantId = null,
        CancellationToken ct = default
    )
    {
        var urlResult = await GetDownloadUrlAsync(fileId, tenantId, ct);
        if (urlResult.IsFailure)
            return Result.Failure<string>(urlResult.Error);

        using var response = await httpClient.GetAsync(urlResult.Value, ct);
        if (!response.IsSuccessStatusCode)
            return Result.Failure<string>(new Error("Email.StorageDownload", "Object download from storage failed."));

        var content = await response.Content.ReadAsStringAsync(ct);
        return Result.Success(content);
    }

    private async Task<Result<T>> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string token,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("CloudStorage call {Path} failed ({Status}).", path, (int)response.StatusCode);
            return Result.Failure<T>(
                new Error("Email.Storage", $"CloudStorage request failed ({(int)response.StatusCode}).")
            );
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(Json, ct);
        return payload is null
            ? Result.Failure<T>(new Error("Email.Storage", "Empty CloudStorage response."))
            : Result.Success(payload);
    }

    private async Task<Result> SendAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("CloudStorage call {Path} failed ({Status}).", path, (int)response.StatusCode);
            return Result.Failure(
                new Error("Email.Storage", $"CloudStorage request failed ({(int)response.StatusCode}).")
            );
        }

        return Result.Success();
    }

    private sealed record InitiateUploadRequestDto(
        string OriginalName,
        string ContentType,
        long SizeBytes,
        string OwnerType,
        Guid? OwnerId,
        string FolderType,
        int? TaxYear
    );

    private sealed record InitiatedUploadResponseDto(
        Guid FileId,
        Uri UploadUrl,
        Dictionary<string, string> FormData,
        DateTime ExpiresAtUtc,
        string Status
    );

    private sealed record DownloadUrlResponseDto(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);
}
