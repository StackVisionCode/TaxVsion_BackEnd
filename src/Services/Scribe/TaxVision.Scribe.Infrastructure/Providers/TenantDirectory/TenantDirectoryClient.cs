using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Scribe.Application.Abstractions;

namespace TaxVision.Scribe.Infrastructure.Providers.TenantDirectory;

/// <summary>Base URL del microservicio Tenant. Solo la usa el backfill de arranque.</summary>
public sealed class TenantClientOptions
{
    public const string SectionName = "Scribe:Tenant";

    // Puerto de Tenant en la flota local (scripts/start-fleet.ps1). En Docker lo pisa Scribe__Tenant__BaseUrl.
    public string BaseUrl { get; set; } = "http://localhost:5217";
}

/// <summary>Una oficina tal como la devuelve <c>GET internal/tenants/directory</c>.</summary>
public sealed record TenantDirectoryItem(Guid Id, string Name, string Subdomain);

public interface ITenantDirectoryClient
{
    /// <summary>
    /// Una pagina del listado. Lista vacia = no hay mas. <b>null = no se pudo leer</b> (Tenant caido,
    /// sin token, 4xx). Confundirlas hace que el backfill reporte "ya esta completo" sin haber preguntado.
    /// </summary>
    Task<IReadOnlyList<TenantDirectoryItem>?> GetPageAsync(int page, int size, CancellationToken ct = default);
}

/// <summary>
/// Lee el listado de oficinas de Tenant por M2M. Lo usa UNICAMENTE el backfill de arranque: el render
/// siempre lee la proyeccion local, porque una caida de Tenant no puede dejar sin correos a nadie.
/// </summary>
public sealed class TenantDirectoryClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    IOptions<TenantClientOptions> options,
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
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("No service token available to read the tenant directory.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{options.Value.BaseUrl.TrimEnd('/')}/internal/tenants/directory?page={page}&size={size}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Tenant directory page {Page} failed ({Status}).", page, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<List<TenantDirectoryItem>>(Json, ct) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Tenant directory page {Page} errored.", page);
            return null;
        }
    }
}
