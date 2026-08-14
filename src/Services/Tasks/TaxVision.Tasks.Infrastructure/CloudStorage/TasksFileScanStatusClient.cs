using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Attachments.Abstractions;

namespace TaxVision.Tasks.Infrastructure.CloudStorage;

public sealed class CloudStorageClientOptions
{
    public const string SectionName = "Tasks:CloudStorage";

    /// <summary>Local: http://localhost:5330. En Docker: http://cloudstorage-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5330";
}

/// <summary>
/// Cliente M2M de solo lectura hacia CloudStorage. Reutiliza el <see cref="IServiceTokenAcquirer"/>
/// del servicio y sólo agrega un HttpClient tipado. Nunca lanza: ante cualquier fallo devuelve
/// <see cref="RemoteFileScanStatus.Unknown"/> y el adjunto se queda como estaba, para reintentarlo
/// en el barrido siguiente.
/// </summary>
internal sealed class TasksFileScanStatusClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<TasksFileScanStatusClient> logger
) : ITaskFileScanStatusClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<RemoteFileScanStatus> GetStatusAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return RemoteFileScanStatus.Unknown;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"storage/internal/files/{fileId:D}/scan-status"
            );
            request.Headers.Authorization = new("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);

            // Un 404 no distingue «lo borraron» de «esta ruta no existe» o «el token mira otro
            // tenant». Desadjuntar por una respuesta ambigua borraría una referencia buena, así que
            // el borrado sólo llega por el estado explícito o por el evento de CloudStorage.
            if (!response.IsSuccessStatusCode)
                return RemoteFileScanStatus.Unknown;

            var dto = await response.Content.ReadFromJsonAsync<ScanStatusDto>(Json, ct);
            return Map(dto?.Status);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not read the scan status of file {FileId} from CloudStorage.", fileId);
            return RemoteFileScanStatus.Unknown;
        }
    }

    /// <summary>
    /// Los estados intermedios del escaneo caen en <c>Unknown</c> a propósito: el archivo todavía
    /// puede acabar disponible o rechazado, y adelantar el veredicto sería inventarlo.
    /// </summary>
    private static RemoteFileScanStatus Map(string? status) =>
        status switch
        {
            "Available" => RemoteFileScanStatus.Available,
            "Infected" => RemoteFileScanStatus.Infected,
            "BlockedByPolicy" => RemoteFileScanStatus.BlockedByPolicy,
            "SoftDeleted" => RemoteFileScanStatus.Deleted,
            _ => RemoteFileScanStatus.Unknown,
        };

    private sealed record ScanStatusDto(string? Status);
}
