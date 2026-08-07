using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Infrastructure.Branding;

namespace TaxVision.Tenant.Infrastructure.Onboarding;

/// <summary>Fase 18 — reusa ITenantServiceTokenAcquirer (mismo client tenant-worker que
/// AuthOnboardingStatusClient) con PlatformTenant.Id: no hay tenant real todavía en este punto de
/// CreateTenantHandler.</summary>
internal sealed class AuthInvitationTokenReferenceClient(
    HttpClient httpClient,
    ITenantServiceTokenAcquirer tokenAcquirer,
    ILogger<AuthInvitationTokenReferenceClient> logger
) : IAuthInvitationTokenReferenceClient
{
    public async Task<Result<Guid>> StoreAsync(string rawToken, CancellationToken ct = default)
    {
        var token = await tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<Guid>(new Error("Tenant.Invitation.Auth", "No Auth credentials available."));

        using var request = new HttpRequestMessage(HttpMethod.Post, "internal/invitations/token-references")
        {
            Content = JsonContent.Create(new { RawToken = rawToken }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Auth invitation token-reference store request returned {StatusCode}.",
                    (int)response.StatusCode
                );
                return Result.Failure<Guid>(
                    new Error(
                        "Tenant.Invitation.TokenReferenceUnavailable",
                        $"Auth returned {(int)response.StatusCode}."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<StoreResponseDto>(ct);
            return payload is null
                ? Result.Failure<Guid>(
                    new Error("Tenant.Invitation.TokenReferenceUnavailable", "Empty response from Auth.")
                )
                : Result.Success(payload.Reference);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Auth invitation token-reference store request failed.");
            return Result.Failure<Guid>(
                new Error("Tenant.Invitation.TokenReferenceUnavailable", "Could not reach Auth.")
            );
        }
    }

    private sealed record StoreResponseDto(Guid Reference);
}
