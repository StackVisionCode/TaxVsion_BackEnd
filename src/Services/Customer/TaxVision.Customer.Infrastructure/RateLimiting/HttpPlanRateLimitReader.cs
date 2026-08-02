using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Caching;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Infrastructure.Imports;

namespace TaxVision.Customer.Infrastructure.RateLimiting;

public sealed class SubscriptionClientOptions
{
    public const string SectionName = "SubscriptionClient";

    /// <summary>Base URL del microservicio Subscription. En Docker: http://subscription-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5360";
}

/// <summary>
/// RateLimit Fase 6 (piloto Customer) — trae el catálogo completo de PlanRateLimits desde
/// Subscription (GET subscriptions/internal/plan-rate-limits) y lo cachea 5 min: el catálogo es
/// global (no por-tenant), así que una sola llamada M2M cubre todos los tenants del proceso.
/// Reusa el token acquirer M2M ya existente de Imports (customer-worker) — Subscription's
/// ServiceOnly policy solo exige actor_type=Service, sin scopes de permiso, así que no hace
/// falta registrar un client M2M dedicado.
/// </summary>
internal sealed class HttpPlanRateLimitReader(
    HttpClient http,
    IServiceTokenAcquirer tokenAcquirer,
    ICacheService cache,
    ILogger<HttpPlanRateLimitReader> logger
) : IPlanRateLimitReader
{
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(5);
    private const string CatalogCacheKey = "ratelimit:subscription-plan-rate-limits-catalog";

    public async Task<PlanRateLimitSnapshot?> GetAsync(
        string planCode,
        RateLimitCategory category,
        CancellationToken ct = default
    )
    {
        var catalog = await cache.GetOrCreateAsync(CatalogCacheKey, FetchCatalogAsync, CatalogTtl, ct);
        return catalog.TryGetValue((planCode, category), out var snapshot) ? snapshot : null;
    }

    private async Task<
        IReadOnlyDictionary<(string PlanCode, RateLimitCategory Category), PlanRateLimitSnapshot>
    > FetchCatalogAsync(CancellationToken ct)
    {
        var empty = new Dictionary<(string, RateLimitCategory), PlanRateLimitSnapshot>();

        // El catálogo es global, no por-tenant — Guid.Empty es el sentinel ya establecido en el
        // repo para llamadas M2M sin tenant real (ver PayFlow Fase 8, PaymentApp checkout).
        var token = await tokenAcquirer.GetTokenAsync(Guid.Empty, ct);
        if (token is null)
        {
            logger.LogWarning("Could not acquire a service token to fetch the plan rate limits catalog; failing open.");
            return empty;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "subscriptions/internal/plan-rate-limits");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Subscription plan-rate-limits catalog request failed with {StatusCode}; failing open.",
                response.StatusCode
            );
            return empty;
        }

        var rows = await response.Content.ReadFromJsonAsync<List<PlanRateLimitRow>>(cancellationToken: ct) ?? [];

        var catalog = new Dictionary<(string, RateLimitCategory), PlanRateLimitSnapshot>();
        foreach (var row in rows)
        {
            if (!Enum.TryParse<RateLimitCategory>(row.Category, out var category))
                continue;
            catalog[(row.PlanCode, category)] = new PlanRateLimitSnapshot(
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
