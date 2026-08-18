using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Onboarding;

/// <summary>
/// PayFlow (Fase 12) — cliente HTTP de <c>GET internal/onboarding/tokens/{reference}/raw</c>.
/// Reusa el mismo <see cref="IServiceTokenAcquirer"/> M2M ya registrado para CloudStorage/Scribe
/// (no está atado a un downstream específico). El onboarding es pre-tenant — a diferencia de esos
/// dos clientes, acá NO se puede pedir el token para <c>evt.TenantId</c> (viene en
/// <c>Guid.Empty</c>, y <c>IssueServiceTokenHandler</c> de Auth rechaza explícitamente
/// <c>TenantId==Guid.Empty</c> con "A tenant is required."): se usa
/// <see cref="PlatformTenant.Id"/> en su lugar, el mismo sentinel que Documents (Fase 10) ya usa
/// para el propio recibo. El endpoint destino solo exige <c>actor_type=Service</c>
/// (<c>[AllowActorTypes(ActorType.Service)]</c>) — no valida el tenant embebido en el token, así
/// que cualquier tenant válido y no-vacío alcanza.
/// </summary>
public sealed class OnboardingTokenClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<OnboardingTokenClient> logger
) : IOnboardingTokenClient
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<Result<string>> ResolveRegistrationUrlAsync(Guid tokenReference, CancellationToken ct = default)
    {
        var token = await tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<string>(
                new Error("Onboarding.TokenClientAuth", "No Auth M2M credentials available.")
            );

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"internal/onboarding/tokens/{tokenReference:D}/raw"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Onboarding token-reference resolution returned {StatusCode} for reference {TokenReference}.",
                (int)response.StatusCode,
                tokenReference
            );
            return Result.Failure<string>(
                new Error("Onboarding.TokenReferenceResolutionFailed", $"Auth returned {(int)response.StatusCode}.")
            );
        }

        var payload = await response.Content.ReadFromJsonAsync<ResolveRegistrationTokenReferenceResponseDto>(
            ResponseJsonOptions,
            ct
        );
        return payload is null || string.IsNullOrWhiteSpace(payload.RegistrationUrl)
            ? Result.Failure<string>(
                new Error("Onboarding.TokenReferenceResolutionFailed", "Empty response from Auth.")
            )
            : Result.Success(payload.RegistrationUrl);
    }

    private sealed record ResolveRegistrationTokenReferenceResponseDto(string RegistrationUrl);
}
