using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;

namespace TaxVision.Signature.Infrastructure.Seed;

/// <summary>Base URL del microservicio Tenant. Solo la usa el backfill de arranque.</summary>
public sealed class TenantClientOptions
{
    public const string SectionName = "Signature:Tenant";

    // Puerto de Tenant en la flota local (scripts/start-fleet.ps1). En Docker lo pisa
    // Signature__Tenant__BaseUrl.
    public string BaseUrl { get; set; } = "http://localhost:5217";
}

/// <summary>Una oficina tal como la devuelve <c>GET internal/tenants/directory</c>.</summary>
public sealed record TenantDirectoryItem(Guid Id, string Name, string Subdomain);

public interface ITenantDirectoryClient
{
    /// <summary>
    /// Una página del listado. Lista vacía = no hay más. <b>null = no se pudo leer</b> (Tenant caído,
    /// sin token, 4xx). Son cosas distintas: confundirlas hace que el backfill reporte "ya está
    /// completo" cuando en realidad no pudo ni preguntar.
    /// </summary>
    Task<IReadOnlyList<TenantDirectoryItem>?> GetPageAsync(int page, int size, CancellationToken ct = default);
}

/// <summary>
/// Lee el listado de oficinas de Tenant por M2M. Lo usa ÚNICAMENTE el backfill de arranque: el sellado
/// y la página pública siempre leen la proyección local, para que una caída de Tenant no frene una firma.
/// </summary>
internal sealed class TenantDirectoryClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<TenantDirectoryClient> logger
) : ITenantDirectoryClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<TenantDirectoryItem>?> GetPageAsync(
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        // El token va contra el tenant de plataforma: el listado cruza todas las oficinas.
        var token = await tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("No service token available; skipping the tenant branding backfill.");
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"internal/tenants/directory?page={page}&size={size}"
        );
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
                return null;
            }

            return await response.Content.ReadFromJsonAsync<List<TenantDirectoryItem>>(Json, ct) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Que Tenant no conteste al arrancar no puede impedir que Signature arranque: sin backfill las
            // firmas siguen saliendo, solo que el certificado de esas oficinas sin su nombre.
            logger.LogWarning(ex, "Could not read the tenant directory; backfill skipped this boot.");
            return null;
        }
    }
}
