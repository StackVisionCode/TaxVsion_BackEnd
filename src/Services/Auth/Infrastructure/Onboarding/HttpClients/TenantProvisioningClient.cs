using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>
/// PayFlow (Fase 15) — dispara la creación del Tenant real
/// (<c>POST tenants/internal/from-onboarding</c>, endpoint que Fase 16 construye). Mismo patrón de
/// JWT in-process que <see cref="ReceiptDocumentClient"/>: fire-and-forget, la Saga
/// (<c>TenantOnboardingProcessManager</c>) no espera el <c>TenantId</c> en esta respuesta — avanza
/// cuando le llega <c>TenantCreatedForOnboardingIntegrationEvent</c> por el bus.
/// </summary>
public sealed class TenantProvisioningClient(
    HttpClient httpClient,
    IJwtTokenGenerator tokens,
    ILogger<TenantProvisioningClient> logger
) : ITenantProvisioningClient
{
    private const string ClientId = "auth-onboarding-saga-tenant";

    public async Task<Result> CreateTenantAsync(
        CreateTenantForOnboardingRequest request,
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
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "tenants/internal/from-onboarding")
            {
                Content = JsonContent.Create(
                    new
                    {
                        onboardingId = request.OnboardingId,
                        officeName = request.OfficeName,
                        subdomain = request.Subdomain,
                        adminEmail = request.AdminEmail,
                    }
                ),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Tenant onboarding provisioning request returned {StatusCode} for onboarding {OnboardingId}.",
                    (int)response.StatusCode,
                    request.OnboardingId
                );
                return Result.Failure(
                    new Error(
                        "TenantProvisioningClient.UnexpectedStatus",
                        $"Tenant returned {(int)response.StatusCode}."
                    )
                );
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "Tenant onboarding provisioning request failed for onboarding {OnboardingId}.",
                request.OnboardingId
            );
            return Result.Failure(new Error("TenantProvisioningClient.RequestFailed", "Could not reach Tenant."));
        }
    }
}
