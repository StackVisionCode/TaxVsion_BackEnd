using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.Registration.Queries;

public sealed record PreviewRegistrationQuery(string Token);

public sealed record PreviewRegistrationResponse(
    string FirstName,
    string LastName,
    string MaskedEmail,
    string? PlanName
);

/// <summary>
/// PayFlow (Fase 13) — muestra al comprador quién es antes de pedirle password/subdomain, sin
/// exponer el OnboardingId. Reusa el mismo par (ISecureTokenService.Hash + normalización a
/// minúsculas) que RegistrationTokenHash.Create ya exige, para poder buscar por el índice único
/// filtrado sin duplicar la validación de forma del hash.
/// </summary>
public static class PreviewRegistrationHandler
{
    public static async Task<Result<PreviewRegistrationResponse>> Handle(
        PreviewRegistrationQuery query,
        ITenantOnboardingRepository onboardings,
        ISecureTokenService tokens,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(query.Token))
            return Result.Failure<PreviewRegistrationResponse>(
                new Error("Onboarding.InvalidToken", "The registration token is invalid.")
            );

        var hash = tokens.Hash(query.Token).ToLowerInvariant();
        var onboarding = await onboardings.GetByRegistrationTokenHashAsync(hash, ct);
        if (onboarding is null)
            return Result.Failure<PreviewRegistrationResponse>(
                new Error("Onboarding.InvalidToken", "The registration token is invalid.")
            );

        if (onboarding.RegistrationTokenUsedAtUtc is not null)
            return Result.Failure<PreviewRegistrationResponse>(
                new Error("Onboarding.TokenUsed", "The registration token was already used.")
            );

        if (
            onboarding.RegistrationTokenExpiresAtUtc is null
            || DateTime.UtcNow >= onboarding.RegistrationTokenExpiresAtUtc
        )
            return Result.Failure<PreviewRegistrationResponse>(
                new Error("Onboarding.TokenExpired", "The registration token has expired.")
            );

        return Result.Success(
            new PreviewRegistrationResponse(
                onboarding.FirstName,
                onboarding.LastName,
                MaskEmail(onboarding.Email),
                // PlanName real no disponible en Auth hasta Fase 16 (catálogo de planes vive en
                // Subscription) — mismo gap ya documentado en OnboardingRegistrationReadyIntegrationEvent.PlanName.
                null
            )
        );
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
            return "***";

        var local = email[..at];
        var visible = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visible}***@{email[(at + 1)..]}";
    }
}
