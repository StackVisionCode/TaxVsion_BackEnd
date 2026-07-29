using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>
/// PayFlow (Fase 15) — dispara la activación de la suscripción
/// (<c>POST subscriptions/internal/activate-from-onboarding</c>, endpoint que Fase 16 construye).
/// Fire-and-forget: la Saga avanza cuando le llega
/// <c>SubscriptionActivatedForOnboardingIntegrationEvent</c> por el bus.
/// </summary>
public sealed class SubscriptionActivationClient(
    HttpClient httpClient,
    IJwtTokenGenerator tokens,
    ILogger<SubscriptionActivationClient> logger
) : ISubscriptionActivationClient
{
    private const string ClientId = "auth-onboarding-saga-subscription";

    public async Task<Result> ActivateAsync(
        ActivateSubscriptionForOnboardingRequest request,
        CancellationToken ct = default
    )
    {
        var token = tokens.GenerateScopedServiceToken(
            PlatformTenant.Id,
            ClientId,
            permissions: [],
            scopes: [],
            audience: "TaxVision.Services",
            lifetimeMinutes: 5
        );

        try
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "subscriptions/internal/activate-from-onboarding"
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

            using var response = await httpClient.SendAsync(httpRequest, ct);
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
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
