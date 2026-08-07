using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Infrastructure.Onboarding.Security;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>
/// PayFlow (Fase 15) — dispara la activación de la suscripción
/// (<c>POST internal/subscriptions/activate-from-onboarding</c>, endpoint que Fase 16 construye).
/// Fire-and-forget: la Saga avanza cuando le llega
/// <c>SubscriptionActivatedForOnboardingIntegrationEvent</c> por el bus.
/// </summary>
public sealed class SubscriptionActivationClient(
    HttpClient httpClient,
    OnboardingServiceTokenCache tokenCache,
    HttpResiliencePipelineRegistry resilience,
    ILogger<SubscriptionActivationClient> logger
) : ISubscriptionActivationClient
{
    private const string ClientId = "auth-onboarding-saga-subscription";

    public async Task<Result> ActivateAsync(
        ActivateSubscriptionForOnboardingRequest request,
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
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "internal/subscriptions/activate-from-onboarding"
            )
            {
                Content = JsonContent.Create(
                    new
                    {
                        onboardingId = request.OnboardingId,
                        tenantId = request.TenantId,
                        planId = request.PlanId,
                    }
                ),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            var breaker = resilience.GetOrCreate(nameof(SubscriptionActivationClient));
            using var response = await breaker.ExecuteAsync(token => httpClient.SendAsync(httpRequest, token), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Subscription activation request returned {StatusCode} for onboarding {OnboardingId}.",
                    (int)response.StatusCode,
                    request.OnboardingId
                );
                return Result.Failure(
                    new Error(
                        "SubscriptionActivationClient.UnexpectedStatus",
                        $"Subscription returned {(int)response.StatusCode}."
                    )
                );
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(
                ex,
                "Subscription activation request failed for onboarding {OnboardingId}.",
                request.OnboardingId
            );
            return Result.Failure(
                new Error("SubscriptionActivationClient.RequestFailed", "Could not reach Subscription.")
            );
        }
    }
}
