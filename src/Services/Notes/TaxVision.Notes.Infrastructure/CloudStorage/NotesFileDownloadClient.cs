using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Results;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Infrastructure.CloudStorage;

/// <summary>
/// Cliente M2M hacia <c>POST storage/files/{id}/download-url</c> (mismo cliente HTTP que
/// <see cref="NotesFileScanStatusClient"/>). Lo dispara una petición de usuario: sin reintentos y sin lanzar.
/// </summary>
public sealed class NotesFileDownloadClient(HttpClient httpClient, IServiceTokenAcquirer tokenAcquirer)
    : INoteFileDownloadClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // 503: CloudStorage o Auth no respondieron; se puede reintentar.
    private static readonly Error Unavailable = new(
        "CloudStorageClient.RequestFailed",
        "The file service is unavailable right now. Try again later."
    );

    public async Task<Result<NoteFileDownloadUrl>> GetDownloadUrlAsync(
        Guid tenantId,
        Guid fileId,
        bool forPreview,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<NoteFileDownloadUrl>(Unavailable);

        try
        {
            var path = $"storage/files/{fileId:D}/download-url" + (forPreview ? "?purpose=preview" : "");
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Authorization = new("Bearer", token);
            using var response = await httpClient.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Failure<NoteFileDownloadUrl>(NoteErrors.NotFound);
            if (!response.IsSuccessStatusCode)
                return Result.Failure<NoteFileDownloadUrl>(Unavailable);

            var dto = await response.Content.ReadFromJsonAsync<DownloadUrlDto>(Json, ct);
            return dto?.DownloadUrl is { } url
                ? Result.Success(new NoteFileDownloadUrl(url, dto.ExpiresAtUtc))
                : Result.Failure<NoteFileDownloadUrl>(Unavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<NoteFileDownloadUrl>(Unavailable);
        }
    }

    private sealed record DownloadUrlDto(Uri? DownloadUrl, DateTime ExpiresAtUtc);
}
