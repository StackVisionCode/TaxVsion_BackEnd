using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Infrastructure.Onboarding.Resilience;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>
/// Llama al endpoint público (<c>[AllowAnonymous]</c>) <c>GET plans</c> de Subscription — no
/// necesita token M2M. Reusa <see cref="SubscriptionClientOptions"/>, ya registrado para
/// <c>SubscriptionActivationClient</c>.
/// <para>
/// Envuelto en <see cref="OnboardingHttpResiliencePipeline"/> (auditoría F14) — es un GET puramente
/// de lectura (sin side-effects), así que el retry es seguro sin ninguna verificación de idempotencia
/// adicional. Antes de este fix, un solo fallo transient del catálogo mientras se procesaba el webhook
/// de pago quedaba impreso para siempre en el recibo/email como "Selected Plan" en vez del nombre real.
/// </para>
/// </summary>
public sealed class PlanCatalogClient(
    HttpClient httpClient,
    OnboardingHttpResiliencePipelineRegistry resilience,
    ILogger<PlanCatalogClient> logger
) : IPlanCatalogClient
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<string?> GetPlanNameAsync(Guid planId, CancellationToken ct = default)
    {
        try
        {
            var breaker = resilience.GetOrCreate(nameof(PlanCatalogClient));
            using var response = await breaker.ExecuteAsync(token => httpClient.GetAsync("plans", token), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Plan catalog lookup returned {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var plans = await response.Content.ReadFromJsonAsync<List<PlanCatalogEntry>>(ResponseJsonOptions, ct);
            return plans?.FirstOrDefault(p => p.Id == planId)?.Name;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(ex, "Plan catalog lookup failed for plan {PlanId}.", planId);
            return null;
        }
    }

    private sealed record PlanCatalogEntry(Guid Id, string Name);
}
