using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Infrastructure.Branding;

namespace TaxVision.Tenant.Infrastructure.Onboarding;

/// <summary>
/// PayFlow (Fase 16) — reusa <see cref="ITenantServiceTokenAcquirer"/> (ya apunta a Auth vía
/// <c>ServiceAuthClientOptions.AuthBaseUrl</c>, el mismo usado para las llamadas a CloudStorage) con
/// <c>PlatformTenant.Id</c> como tenant del token: no hay un tenant real todavía en este punto del
/// flujo de onboarding.
/// </summary>
internal sealed class AuthOnboardingStatusClient(
    HttpClient httpClient,
    ITenantServiceTokenAcquirer tokenAcquirer,
    ILogger<AuthOnboardingStatusClient> logger
) : IAuthOnboardingStatusClient
{
    public async Task<Result<OnboardingStatusSnapshot>> GetStatusAsync(
        Guid onboardingId,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<OnboardingStatusSnapshot>(
                new Error("Tenant.Onboarding.Auth", "No Auth credentials available.")
            );

        using var request = new HttpRequestMessage(HttpMethod.Get, $"auth/internal/onboarding/{onboardingId}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Auth onboarding status request returned {StatusCode} for onboarding {OnboardingId}.",
                    (int)response.StatusCode,
                    onboardingId
                );
                return Result.Failure<OnboardingStatusSnapshot>(
                    new Error("Tenant.Onboarding.StatusUnavailable", $"Auth returned {(int)response.StatusCode}.")
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<StatusDto>(ct);
            return payload is null
                ? Result.Failure<OnboardingStatusSnapshot>(
                    new Error("Tenant.Onboarding.StatusUnavailable", "Empty response from Auth.")
                )
                : Result.Success(new OnboardingStatusSnapshot(payload.Status, payload.PaymentCompletedAtUtc));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Auth onboarding status request failed for onboarding {OnboardingId}.", onboardingId);
            return Result.Failure<OnboardingStatusSnapshot>(
                new Error("Tenant.Onboarding.StatusUnavailable", "Could not reach Auth.")
            );
        }
    }

    private sealed record StatusDto(string Status, DateTime? PaymentCompletedAtUtc);
}
