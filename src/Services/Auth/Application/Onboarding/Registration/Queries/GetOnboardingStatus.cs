using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.Registration.Queries;

public sealed record GetOnboardingStatusQuery(string Token);

public sealed record OnboardingStatusResponse(
    string Status,
    string? CurrentStep,
    string? FailureReason,
    string? FailureCode,
    string? RedirectUrl
);

/// <summary>
/// PayFlow (Fase 13) — polling público del progreso de provisioning. Nunca expone OnboardingId
/// (§ literal del plan): el token sigue siendo la única clave que el frontend conoce. El
/// RegistrationTokenHash no se borra al consumirse (ConsumeRegistrationToken solo estampa
/// RegistrationTokenUsedAtUtc), así que este mismo token sigue resolviendo el onboarding durante
/// todo el provisioning y después de Completed.
/// </summary>
public static class GetOnboardingStatusHandler
{
    public static async Task<Result<OnboardingStatusResponse>> Handle(
        GetOnboardingStatusQuery query,
        ITenantOnboardingRepository onboardings,
        ISecureTokenService tokens,
        IOptions<OnboardingOptions> onboardingOptions,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(query.Token))
            return Result.Failure<OnboardingStatusResponse>(
                new Error("Onboarding.InvalidToken", "The registration token is invalid.")
            );

        var hash = tokens.Hash(query.Token).ToLowerInvariant();
        var onboarding = await onboardings.GetByRegistrationTokenHashAsync(hash, ct);
        if (onboarding is null)
            return Result.Failure<OnboardingStatusResponse>(
                new Error("Onboarding.InvalidToken", "The registration token is invalid.")
            );

        string? redirectUrl = null;
        if (
            onboarding.Status == TenantOnboardingStatus.Completed
            && !string.IsNullOrWhiteSpace(onboarding.RequestedSubdomain)
        )
            redirectUrl = $"https://{onboarding.RequestedSubdomain}.{onboardingOptions.Value.TenantBaseDomain}";

        var exposeFailure =
            onboarding.Status is TenantOnboardingStatus.ProvisioningFailed or TenantOnboardingStatus.ManualReview;

        return Result.Success(
            new OnboardingStatusResponse(
                onboarding.Status.ToString(),
                onboarding.Status == TenantOnboardingStatus.Provisioning ? onboarding.CurrentStep.ToString() : null,
                exposeFailure ? onboarding.FailureReason : null,
                exposeFailure ? onboarding.FailureCode : null,
                redirectUrl
            )
        );
    }
}
