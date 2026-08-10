using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Infrastructure.Onboarding.Security;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>M2M Auth→Subscription (<c>GET subscriptions/internal/plans/{planId}/pricing</c>, ServiceOnly,
/// audience TaxVision.Services). Token con <c>PlatformTenant.Id</c> (comprador anónimo, pre-tenant).</summary>
public sealed class OnboardingPlanPricingClient(
    HttpClient httpClient,
    OnboardingServiceTokenCache tokenCache,
    HttpResiliencePipelineRegistry resilience,
    ILogger<OnboardingPlanPricingClient> logger
) : IOnboardingPlanPricingClient
{
    private const string ClientId = "auth-onboarding-pricing";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<Result<OnboardingPlanPrice>> GetGrossPriceAsync(
        Guid planId,
        string billingCycle,
        CancellationToken ct = default
    )
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

        try
        {
            // subscription-api sirve rutas LITERALES sin path-base (verificado: /internal/plans/.../pricing
            // → 401=existe; /subscriptions/internal/... → 404). El BaseUrl ya es el host.
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/plans/{planId}/pricing?cycle={Uri.EscapeDataString(billingCycle)}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            var breaker = resilience.GetOrCreate(nameof(OnboardingPlanPricingClient));
            using var response = await breaker.ExecuteAsync(inner => httpClient.SendAsync(request, inner), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Subscription pricing returned {StatusCode} for plan {PlanId}.",
                    (int)response.StatusCode,
                    planId
                );
                return Result.Failure<OnboardingPlanPrice>(
                    new Error(
                        "Onboarding.Pricing.UnexpectedStatus",
                        $"Subscription returned {(int)response.StatusCode}."
                    )
                );
            }

            var dto = await response.Content.ReadFromJsonAsync<PricingDto>(Json, ct);
            return dto is null
                ? Result.Failure<OnboardingPlanPrice>(
                    new Error("Onboarding.Pricing.Empty", "Subscription returned an empty pricing response.")
                )
                : Result.Success(new OnboardingPlanPrice(dto.PriceCents, dto.Currency));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(ex, "Subscription pricing request failed for plan {PlanId}.", planId);
            return Result.Failure<OnboardingPlanPrice>(
                new Error("Onboarding.Pricing.RequestFailed", "Could not reach Subscription.")
            );
        }
    }

    private sealed record PricingDto(Guid PlanId, long PriceCents, string Currency);
}
