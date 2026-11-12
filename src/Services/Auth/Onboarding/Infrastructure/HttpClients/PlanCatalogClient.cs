using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Onboarding.Application.Abstractions;

namespace TaxVision.Auth.Onboarding.Infrastructure.HttpClients;

/// <summary>
/// M2M Auth→Subscription (<c>GET internal/plans/{planId}</c>, ServiceOnly, audience TaxVision.Services). Pide el
/// plan por id para que recibos y correos lo nombren aunque ya no esté a la venta; el listado público solo trae
/// lo que se vende. Token con <c>PlatformTenant.Id</c>, como <see cref="OnboardingPlanPricingClient"/>.
/// <para>
/// Envuelto en <see cref="HttpResiliencePipeline"/> (auditoría F14 → F24): es un GET de solo lectura, así que el
/// reintento es seguro. Sin él, un fallo pasajero quedaba impreso en el recibo como "Selected Plan".
/// </para>
/// </summary>
public sealed class PlanCatalogClient(
    HttpClient httpClient,
    ServiceTokenCache tokenCache,
    HttpResiliencePipelineRegistry resilience,
    ILogger<PlanCatalogClient> logger
) : IPlanCatalogClient
{
    private const string ClientId = "auth-onboarding-catalog";

    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<string?> GetPlanNameAsync(Guid planId, CancellationToken ct = default)
    {
        try
        {
            var token = await tokenCache.GetOrCreateAsync(
                PlatformTenant.Id,
                ClientId,
                permissions: [],
                scopes: [],
                audience: "TaxVision.Services",
                lifetimeMinutes: 5,
                ct
            );

            var breaker = resilience.GetOrCreate(nameof(PlanCatalogClient));
            using var response = await breaker.ExecuteAsync(
                async inner =>
                {
                    // Un mensaje por intento: el pipeline puede reintentar.
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/plans/{planId}");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                    return await httpClient.SendAsync(request, inner);
                },
                ct
            );
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Plan catalog lookup returned {StatusCode} for plan {PlanId}.",
                    (int)response.StatusCode,
                    planId
                );
                return null;
            }

            var plan = await response.Content.ReadFromJsonAsync<PlanSummary>(ResponseJsonOptions, ct);
            return plan?.Name;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(ex, "Plan catalog lookup failed for plan {PlanId}.", planId);
            return null;
        }
    }

    private sealed record PlanSummary(Guid Id, string Code, string Name);
}
