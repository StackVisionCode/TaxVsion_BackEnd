using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.TokenReferences.Queries;

public sealed record ResolveRegistrationTokenReferenceQuery(Guid TokenReference);

public sealed record ResolveRegistrationTokenReferenceResponse(string RegistrationUrl);

/// <summary>PayFlow (Fase 9) — el lado de lectura del endpoint M2M one-shot
/// (<c>GET /internal/onboarding/tokens/{reference}/raw</c>).
/// <para>
/// Auditoría F15 — hasta acá el raw token se consumía (GETDEL) en la misma llamada, así que un
/// retry de Notification tras un fallo transient (timeout, 5xx) siempre encontraba la referencia
/// ya borrada. Ahora usa <see cref="ITokenReferenceStore.PeekAsync"/>: lee sin borrar, respetando el
/// TTL original (30s) — la ventana de exposición del raw token no cambia, pero un reintento dentro
/// de esa ventana ya no falla espuriamente.
/// </para>
/// </summary>
public static class ResolveRegistrationTokenReferenceHandler
{
    public static async Task<Result<ResolveRegistrationTokenReferenceResponse>> Handle(
        ResolveRegistrationTokenReferenceQuery query,
        ITokenReferenceStore tokenReferences,
        IOptions<OnboardingOptions> onboardingOptions,
        CancellationToken ct
    )
    {
        var rawToken = await tokenReferences.PeekAsync(query.TokenReference, ct);
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
