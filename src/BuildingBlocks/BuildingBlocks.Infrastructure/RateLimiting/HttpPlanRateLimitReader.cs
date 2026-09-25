using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Caching;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.RateLimiting;

public sealed class SubscriptionClientOptions
{
    public const string SectionName = "SubscriptionClient";

    /// <summary>Base URL del microservicio Subscription. En Docker: http://subscription-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5360";
}

/// <summary>
/// RateLimit Fase 6 (piloto Customer) / Fase 1 (extracción BuildingBlocks) — trae el catálogo
/// completo de PlanRateLimits desde Subscription (GET internal/plan-rate-limits) y
/// lo cachea 5 min: el catálogo es global (no por-tenant), así que una sola llamada M2M cubre
/// todos los tenants del proceso. Depende de <see cref="IServiceTokenAcquirer"/> (contrato
/// compartido, F25) — cada servicio inyecta su propio acquirer M2M ya existente; Subscription's
/// ServiceOnly policy solo exige actor_type=Service, sin scopes de permiso, así que no hace falta
/// registrar un client M2M dedicado.
/// </summary>
public sealed class HttpPlanRateLimitReader(
    HttpClient http,
    IServiceTokenAcquirer tokenAcquirer,
    ICacheService cache,
    ILogger<HttpPlanRateLimitReader> logger
) : IPlanRateLimitReader
{
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LastGoodTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan UnavailableBackoff = TimeSpan.FromSeconds(30);
    private const string CatalogCacheKey = "ratelimit:subscription-plan-rate-limits-catalog";
    private const string LastGoodCacheKey = CatalogCacheKey + ":last-good";
    private const string UnavailableCacheKey = CatalogCacheKey + ":unavailable";

    public async Task<PlanRateLimitSnapshot?> GetAsync(
        string planCode,
        RateLimitCategory category,
        CancellationToken ct = default
    )
    {
        var catalog = await GetCatalogAsync(ct);
        return catalog is not null && catalog.TryGetValue(CatalogKey(planCode, category), out var snapshot)
            ? snapshot
            : null;
    }

    // Antes un fallo devolvía un catálogo vacío que quedaba cacheado 5 minutos: todos los tenants caían
    // a la cuota base (la mitad de starter) aunque Subscription hubiera vuelto a los pocos segundos.
    // Ahora un fallo sirve el último catálogo bueno y, si no hay ninguno, se reintenta a los 30 s en vez
    // de consultar Subscription en cada request.
    private async Task<IReadOnlyDictionary<string, PlanRateLimitSnapshot>?> GetCatalogAsync(CancellationToken ct)
    {
        if (await cache.GetAsync<bool>(UnavailableCacheKey, ct))
            return null;

        try
        {
            return await cache.GetOrCreateAsync(CatalogCacheKey, FetchCatalogOrLastGoodAsync, CatalogTtl, ct);
        }
        catch (CatalogUnavailableException)
        {
            await cache.SetAsync(UnavailableCacheKey, true, UnavailableBackoff, ct);
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, PlanRateLimitSnapshot>> FetchCatalogOrLastGoodAsync(
        CancellationToken ct
    )
    {
        var fetched = await FetchCatalogAsync(ct);
        if (fetched is not null)
        {
            await cache.SetAsync(LastGoodCacheKey, fetched, LastGoodTtl, ct);
            return fetched;
        }

        var lastGood = await cache.GetAsync<Dictionary<string, PlanRateLimitSnapshot>>(LastGoodCacheKey, ct);
        if (lastGood is not null)
        {
            logger.LogWarning("Serving the last known plan rate limits catalog while Subscription is unavailable.");
            return lastGood;
        }

        throw new CatalogUnavailableException();
    }

    private sealed class CatalogUnavailableException : Exception { }

    // Clave compuesta serializada a string: un Dictionary con ValueTuple como clave no es
    // serializable por System.Text.Json (bug real encontrado en la verificación de Fase 0 — nunca
    // se había ejercitado antes porque el fetch del catálogo siempre fallaba antes de llegar a
    // cachearse, ver fix de PlatformTenant.Id más abajo).
    private static string CatalogKey(string planCode, RateLimitCategory category) => $"{planCode}:{category}";

    /// <summary>Null ante cualquier fallo (token, status): el caller decide entre último bueno o backoff.</summary>
    private async Task<Dictionary<string, PlanRateLimitSnapshot>?> FetchCatalogAsync(CancellationToken ct)
    {
        // El catálogo es global, no por-tenant — PlatformTenant.Id es el sentinel real para
        // llamadas M2M sin tenant real (Guid.Empty NO sirve: IssueServiceTokenHandler en Auth lo
        // rechaza incondicionalmente con Auth.InvalidClient/401 antes de validar el cliente, bug
        // real encontrado en la verificación de Fase 0 — el catálogo nunca se resolvía y el
        // resolver caía siempre a BaseQuota vía fail-open, sin importar el plan del tenant).
        var token = await tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
        if (token is null)
        {
            logger.LogWarning("Could not acquire a service token to fetch the plan rate limits catalog; failing open.");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "internal/plan-rate-limits");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Subscription plan-rate-limits catalog request failed with {StatusCode}; failing open.",
                response.StatusCode
            );
            return null;
        }

        var rows = await response.Content.ReadFromJsonAsync<List<PlanRateLimitRow>>(cancellationToken: ct) ?? [];

        var catalog = new Dictionary<string, PlanRateLimitSnapshot>();
        foreach (var row in rows)
        {
            if (!Enum.TryParse<RateLimitCategory>(row.Category, out var category))
                continue;
            catalog[CatalogKey(row.PlanCode, category)] = new PlanRateLimitSnapshot(
                row.MultiplierOverride,
                row.HardOverridePerMinute
            );
        }

        return catalog;
    }

    private sealed record PlanRateLimitRow(
        string PlanCode,
        string Category,
        decimal MultiplierOverride,
        int? HardOverridePerMinute
    );
}
