using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.TokenReferences.Queries;

public sealed record ResolveRegistrationTokenReferenceQuery(Guid TokenReference);

public sealed record ResolveRegistrationTokenReferenceResponse(string RegistrationUrl);

/// <summary>PayFlow (Fase 9) — el lado de lectura del endpoint M2M one-shot
/// (<c>GET /auth/internal/onboarding/tokens/{reference}/raw</c>). El raw token se consume (se
/// borra de Redis) en la misma llamada — un segundo intento con la misma referencia siempre
/// falla, por diseño.</summary>
public static class ResolveRegistrationTokenReferenceHandler
{
    public static async Task<Result<ResolveRegistrationTokenReferenceResponse>> Handle(
        ResolveRegistrationTokenReferenceQuery query,
        ITokenReferenceStore tokenReferences,
        IOptions<OnboardingOptions> onboardingOptions,
        CancellationToken ct
    )
    {
        var rawToken = await tokenReferences.ConsumeAsync(query.TokenReference, ct);
        if (rawToken is null)
            return Result.Failure<ResolveRegistrationTokenReferenceResponse>(
                new Error(
                    "Onboarding.TokenReferenceNotFound",
                    "This token reference is invalid, expired, or was already used."
                )
            );

        var registrationUrl =
            $"{onboardingOptions.Value.RegistrationUrlBase.TrimEnd('/')}/register?token={Uri.EscapeDataString(rawToken)}";

        return Result.Success(new ResolveRegistrationTokenReferenceResponse(registrationUrl));
    }
}
