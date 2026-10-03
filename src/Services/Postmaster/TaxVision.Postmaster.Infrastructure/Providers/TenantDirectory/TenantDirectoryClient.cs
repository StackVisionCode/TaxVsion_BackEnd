using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Postmaster.Infrastructure.Providers.Assets;

namespace TaxVision.Postmaster.Infrastructure.Providers.TenantDirectory;

/// <summary>Base URL del microservicio Tenant. Solo la usa el backfill de arranque.</summary>
public sealed class TenantClientOptions
{
    public const string SectionName = "Postmaster:Tenant";

    // Puerto de Tenant en la flota local (scripts/start-fleet.ps1). En Docker lo pisa
    // Postmaster__Tenant__BaseUrl.
    public string BaseUrl { get; set; } = "http://localhost:5217";
}

/// <summary>Una oficina tal como la devuelve <c>GET internal/tenants/directory</c>.</summary>
public sealed record TenantDirectoryItem(Guid Id, string Name, string Subdomain);

public interface ITenantDirectoryClient
{
    /// <summary>Una página del listado. Lista vacía = no hay más (o no se pudo leer; ver el log).</summary>
    Task<IReadOnlyList<TenantDirectoryItem>> GetPageAsync(int page, int size, CancellationToken ct = default);
}

/// <summary>
/// Lee el listado de oficinas de Tenant por M2M. Lo usa ÚNICAMENTE el backfill de arranque: el
/// camino de envío siempre lee la proyección local, porque meter una llamada de red ahí haría que
/// una caída de Tenant dejara a la plataforma sin mandar un solo correo.
/// </summary>
public sealed class TenantDirectoryClient(
    HttpClient httpClient,
    IPostmasterServiceTokenAcquirer tokenAcquirer,
    IOptions<TenantClientOptions> options,
    ILogger<TenantDirectoryClient> logger
) : ITenantDirectoryClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<TenantDirectoryItem>> GetPageAsync(
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        // El token va contra el tenant de plataforma: el listado cruza todas las oficinas, así que
        // pedirlo en nombre de una de ellas sería mentir sobre quién pregunta.
        var token = await tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("No service token available; skipping the tenant directory backfill.");
            return [];
        }

        var url = $"{options.Value.BaseUrl.TrimEnd('/')}/internal/tenants/directory?page={page}&size={size}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Tenant directory page {Page} responded {StatusCode}; backfill stops here.",
                    page,
                    (int)response.StatusCode
                );
                return [];
            }

            return await response.Content.ReadFromJsonAsync<List<TenantDirectoryItem>>(Json, ct) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Que Tenant no conteste al arrancar no puede impedir que Postmaster arranque: sin
            // backfill los correos siguen saliendo, solo que sin el nombre de la oficina en el From.
            logger.LogWarning(ex, "Could not read the tenant directory; backfill skipped this boot.");
            return [];
        }
    }
}
