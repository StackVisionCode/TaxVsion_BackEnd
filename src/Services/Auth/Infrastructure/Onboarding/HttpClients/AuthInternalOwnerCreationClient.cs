using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>
/// PayFlow (Fase 15) — loopback HTTP hacia el propio Auth
/// (<c>POST auth/internal/tenants/{tenantId}/owners</c>, endpoint que Fase 16 construye). Deliberado:
/// el password nunca debe cruzar el bus de mensajería de Wolverine, así que en vez de que la Saga
/// invoque un command local que reciba el <see cref="CreateTenantOwnerForOnboardingRequest.PasswordHashReference"/>,
/// pasa por este canal HTTP interno igual que cualquier otro paso M2M — el endpoint receptor es el
/// único punto que canjea la referencia contra <c>ITokenReferenceStore</c>. La base URL reusa
/// <c>OnboardingOptions.AuthPublicBaseUrl</c> (ya usada por Fase 11 para el link mediador de
/// descarga del recibo) en vez de una nueva options class — es el mismo "origen público de Auth".
/// </summary>
public sealed class AuthInternalOwnerCreationClient(
    HttpClient httpClient,
    IJwtTokenGenerator tokens,
    ILogger<AuthInternalOwnerCreationClient> logger
) : IAuthInternalOwnerCreationClient
{
    private const string ClientId = "auth-onboarding-saga-owner";

    public async Task<Result> CreateOwnerAsync(
        CreateTenantOwnerForOnboardingRequest request,
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
                $"auth/internal/tenants/{request.TenantId}/owners"
            )
            {
                Content = JsonContent.Create(
                    new
                    {
                        onboardingId = request.OnboardingId,
                        email = request.Email,
                        firstName = request.FirstName,
                        lastName = request.LastName,
                        passwordHashReference = request.PasswordHashReference,
                    }
                ),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Auth internal owner-creation request returned {StatusCode} for onboarding {OnboardingId}.",
                    (int)response.StatusCode,
                    request.OnboardingId
                );
                return Result.Failure(
                    new Error(
                        "AuthInternalOwnerCreationClient.UnexpectedStatus",
                        $"Auth returned {(int)response.StatusCode}."
                    )
                );
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "Auth internal owner-creation request failed for onboarding {OnboardingId}.",
                request.OnboardingId
            );
            return Result.Failure(
                new Error(
                    "AuthInternalOwnerCreationClient.RequestFailed",
                    "Could not reach Auth's internal owner endpoint."
                )
            );
        }
    }
}
