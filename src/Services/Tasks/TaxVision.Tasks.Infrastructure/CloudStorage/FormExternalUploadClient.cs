using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

namespace TaxVision.Tasks.Infrastructure.CloudStorage;

internal sealed class FormExternalUploadClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<FormExternalUploadClient> logger
) : IFormExternalUploadClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly Error Unavailable = new(
        "FormRequest.UploadUnavailable",
        "The attachment upload service is unavailable."
    );

    public async Task<Result<ExternalFormUploadResponse>> InitiateAsync(
        ExternalFormUploadRequest request,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(request.TenantId, ct);
        if (string.IsNullOrWhiteSpace(token))
            return Result.Failure<ExternalFormUploadResponse>(Unavailable);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "storage/files/uploads")
            {
                Content = JsonContent.Create(new
                {
                    request.OriginalName,
                    request.ContentType,
                    request.SizeBytes,
                    request.OwnerType,
                    request.OwnerId,
                    FolderType = "ClientForms",
                    TaxYear = (int?)null,
                }),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
                return Result.Failure<ExternalFormUploadResponse>(Unavailable);
            var body = await response.Content.ReadFromJsonAsync<InitiatedUploadDto>(Json, ct);
            return body is null
                ? Result.Failure<ExternalFormUploadResponse>(Unavailable)
                : Result.Success(new ExternalFormUploadResponse(
                    body.FileId,
                    body.UploadUrl,
                    body.FormData,
                    body.ExpiresAtUtc
                ));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Could not initiate external form upload for tenant {TenantId}.", request.TenantId);
            return Result.Failure<ExternalFormUploadResponse>(Unavailable);
        }
    }

    public async Task<Result> CompleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(token))
            return Result.Failure(Unavailable);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"storage/files/{fileId:D}/complete");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(message, ct);
            return response.IsSuccessStatusCode ? Result.Success() : Result.Failure(Unavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not complete external form upload {FileId}.", fileId);
            return Result.Failure(Unavailable);
        }
    }

    private sealed record InitiatedUploadDto(
        Guid FileId,
        Uri UploadUrl,
        IReadOnlyDictionary<string, string> FormData,
        DateTime ExpiresAtUtc
    );
}
