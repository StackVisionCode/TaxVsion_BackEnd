using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using TaxVision.Notes.Application.Notes.Abstractions;

namespace TaxVision.Notes.Infrastructure.CloudStorage;

public sealed class NotesCloudStorageClientOptions
{
    public const string SectionName = "Notes:CloudStorage";

    /// <summary>Local: http://localhost:5330. En Docker: http://cloudstorage-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5330";
}

/// <summary>
/// Cliente M2M de solo lectura hacia <c>GET internal/files/{id}/scan-status</c> (mismo patrón que Tasks).
/// A diferencia de Tasks, un fallo se lanza: el comando se reintenta y el job vuelve a pasar.
/// </summary>
internal sealed class NotesFileScanStatusClient(HttpClient httpClient, IServiceTokenAcquirer tokenAcquirer)
    : INoteFileScanStatusClient
{
    // Código de CloudStorage para "no existe en este tenant"; un 404 sin él (ruta, versión) no cuenta.
    private const string FileNotFoundCode = "File.NotFound";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RemoteFileStatus> GetStatusAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            throw new HttpRequestException("No service token available to query CloudStorage.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/files/{fileId:D}/scan-status");
        request.Headers.Authorization = new("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound && await IsFileNotFoundAsync(response, ct))
            return RemoteFileStatus.NotFound;

        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<ScanStatusDto>(Json, ct);
        return Map(dto?.Status);
    }

    private static async Task<bool> IsFileNotFoundAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorDto>(Json, ct);
            return string.Equals(error?.Code, FileNotFoundCode, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static RemoteFileStatus Map(string? status) =>
        status switch
        {
            "Available" => RemoteFileStatus.Available,
            "Infected" => RemoteFileStatus.Infected,
            "BlockedByPolicy" => RemoteFileStatus.BlockedByPolicy,
            "ScanFailed" => RemoteFileStatus.ScanFailed,
            "PendingReview" => RemoteFileStatus.PendingReview,
            "SoftDeleted" => RemoteFileStatus.Deleted,
            _ => RemoteFileStatus.Scanning,
        };

    private sealed record ScanStatusDto(string? Status);

    private sealed record ErrorDto(string? Code);
}
