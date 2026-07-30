using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Infrastructure.Onboarding.Security;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>
/// PayFlow (Fase 14) — mismo patrón deliberado que <see cref="ReceiptDocumentClient"/>: Auth es el
/// propio emisor de tokens M2M, genera el JWT de servicio en el mismo proceso. El endpoint de
/// Tenant (<c>InternalTenantAvailabilityController</c>) sólo exige <c>actor_type=Service</c>.
/// </summary>
public sealed class TenantSubdomainAvailabilityClient(
    HttpClient httpClient,
    OnboardingServiceTokenCache tokenCache,
    ILogger<TenantSubdomainAvailabilityClient> logger
) : ITenantSubdomainAvailabilityClient
{
    private const string ClientId = "auth-onboarding-subdomain-check";
    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record SubdomainAvailabilityResponse(bool Taken);

    public async Task<Result<bool>> IsTakenAsync(string slug, CancellationToken ct = default)
    {
        var token = tokenCache.GetOrCreate(
            PlatformTenant.Id,
            ClientId,
            permissions: [],
            scopes: [],
            audience: "TaxVision.Services",
            lifetimeMinutes: 5
        );

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"tenants/internal/subdomain-available?slug={Uri.EscapeDataString(slug)}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Tenant subdomain-available request returned {StatusCode} for slug {Slug}.",
                    (int)response.StatusCode,
                    slug
                );
                return Result.Failure<bool>(
                    new Error(
                        "TenantSubdomainAvailabilityClient.UnexpectedStatus",
                        $"Tenant returned {(int)response.StatusCode}."
                    )
                );
            }

            var body = await response.Content.ReadFromJsonAsync<SubdomainAvailabilityResponse>(ResponseJsonOptions, ct);
            return Result.Success(body?.Taken ?? true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Tenant subdomain-available request failed for slug {Slug}.", slug);
            return Result.Failure<bool>(
                new Error("TenantSubdomainAvailabilityClient.RequestFailed", "Could not reach Tenant.")
            );
        }
    }
}
