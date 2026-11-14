using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;

namespace TaxVision.FinancialInsights.Infrastructure.Upstream;

/// <summary>
/// Metadatos del archivo en CloudStorage con el token del usuario: CloudStorage decide si lo ve. Es la única
/// consulta remota del registro, porque el estado del escaneo y el dueño viven allá.
/// </summary>
public sealed class CloudStorageImportFileGateway(HttpClient http, IHttpContextAccessor context) : IImportFileGateway
{
    private static readonly string[] OwnerTypes = ["Tenant", "Customer", "User", "Signature", "Invoice", "Communication"];

    private static readonly string[] FileStatuses =
    [
        "PendingUpload",
        "PendingScan",
        "Scanning",
        "Available",
        "Infected",
        "ScanFailed",
        "SoftDeleted",
        "BlockedByPolicy",
        "PendingReview",
    ];

    public async Task<ImportFileState> CheckAsync(Guid fileId, Guid customerId, CancellationToken ct = default)
    {
        var bearer = RequestBearer.From(context);
        if (bearer is null || http.BaseAddress is null)
            return ImportFileState.Unavailable;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"storage/files/{fileId:D}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                return ImportFileState.Ineligible;
            if (!response.IsSuccessStatusCode)
                return ImportFileState.Unavailable;

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var file = json.RootElement;
            if (!file.TryGetProperty("id", out var id) || !id.TryGetGuid(out var received) || received != fileId)
                return ImportFileState.Ineligible;

            var owner = ReadEnum(file, "ownerType", OwnerTypes);
            var status = ReadEnum(file, "status", FileStatuses);
            var ownerId = file.TryGetProperty("ownerId", out var o) && o.ValueKind == JsonValueKind.String && o.TryGetGuid(out var parsed)
                ? parsed
                : (Guid?)null;
            var detected = ReadString(file, "detectedContentType");
            var declared = ReadString(file, "declaredContentType");
            var size = file.TryGetProperty("sizeBytes", out var bytes) && bytes.TryGetInt64(out var value) ? value : 0;

            if (ImportFilePolicy.CanRegister(owner, ownerId, customerId, status, detected, size))
                return ImportFileState.Ready;
            return ImportFilePolicy.CanAwaitScan(owner, ownerId, customerId, status, declared, size)
                ? ImportFileState.AwaitingScan
                : ImportFileState.Ineligible;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return ImportFileState.Unavailable;
        }
    }

    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    // CloudStorage puede serializar el enum como texto o como número.
    private static string? ReadEnum(JsonElement parent, string name, string[] values)
    {
        if (!parent.TryGetProperty(name, out var element))
            return null;
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString();
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var index) && index >= 0 && index < values.Length
            ? values[index]
            : null;
    }
}

internal static class RequestBearer
{
    public static string? From(IHttpContextAccessor context)
    {
        var header = context.HttpContext?.Request.Headers.Authorization.ToString();
        return header is { Length: > 7 } && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..].Trim()
            : null;
    }
}
