using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.Registration.Queries;

public sealed record GetInternalOnboardingStatusQuery(Guid OnboardingId);

public sealed record InternalOnboardingStatusResponse(string Status, DateTime? PaymentCompletedAtUtc);

/// <summary>
/// PayFlow (Fase 16) — M2M-only, distinto del <c>/onboarding/status</c> público (Fase 13): este lo
/// consulta Tenant (<c>tenants/internal/from-onboarding</c>) por <c>OnboardingId</c> directo, no por
/// token, para confirmar que el onboarding está realmente en <c>Provisioning</c> con el pago
/// confirmado antes de crear un Tenant real — defensa en profundidad contra que cualquier otro
/// cliente M2M con audience <c>TaxVision.Services</c> mine tenants arbitrarios.
/// </summary>
public static class GetInternalOnboardingStatusHandler
{
    public static async Task<Result<InternalOnboardingStatusResponse>> Handle(
        GetInternalOnboardingStatusQuery query,
        ITenantOnboardingRepository onboardings,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(query.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure<InternalOnboardingStatusResponse>(
                new Error("Onboarding.NotFound", "The onboarding was not found.")
            );

        return Result.Success(
            new InternalOnboardingStatusResponse(onboarding.Status.ToString(), onboarding.PaymentCompletedAtUtc)
        );
    }
}
