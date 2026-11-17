using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Referrals.Domain.Attributions;

/// <summary>
/// Declaración de referido del alta que Growth descartó (código inválido o revocado, programa inactivo…). El pago del
/// alta la consulta para no reintentar un referido que nunca tendrá atribución. Su dueño es el propio alta, como la
/// atribución.
/// </summary>
public sealed class OnboardingReferralRejection : TenantEntity
{
    public const int ReasonCodeMaxLength = 100;

    public Guid OnboardingId { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public DateTime RejectedAtUtc { get; private set; }

    private OnboardingReferralRejection() { }

    public static Result<OnboardingReferralRejection> Create(Guid onboardingId, string reasonCode, DateTime nowUtc)
    {
        if (onboardingId == Guid.Empty)
            return Result.Failure<OnboardingReferralRejection>(
                new Error("OnboardingReferralRejection.InvalidOnboarding", "An onboarding is required.")
            );

        if (string.IsNullOrWhiteSpace(reasonCode))
            return Result.Failure<OnboardingReferralRejection>(
                new Error("OnboardingReferralRejection.InvalidReason", "A reason code is required.")
            );

        var trimmed = reasonCode.Trim();
        var rejection = new OnboardingReferralRejection
        {
            OnboardingId = onboardingId,
            ReasonCode = trimmed.Length > ReasonCodeMaxLength ? trimmed[..ReasonCodeMaxLength] : trimmed,
            RejectedAtUtc = nowUtc,
        };
        rejection.SetTenant(onboardingId);
        return Result.Success(rejection);
    }
}
