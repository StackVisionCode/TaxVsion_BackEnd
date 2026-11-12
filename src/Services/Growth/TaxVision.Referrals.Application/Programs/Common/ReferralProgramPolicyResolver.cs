using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Programs.CreateTenantReferralProgram;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Programs.Common;

/// <summary>
/// Combina un <see cref="TenantReferralProgramPolicyInput"/> parcial con una política base
/// (los defaults al crear; la política vigente al actualizar) y valida el resultado en el dominio.
/// Un campo omitido conserva el valor de la base.
/// </summary>
internal static class ReferralProgramPolicyResolver
{
    public static Result<ReferralProgramPolicy> Resolve(
        TenantReferralProgramPolicyInput? input,
        ReferralProgramPolicy baseline
    )
    {
        var rewardType = input?.RewardType ?? baseline.RewardType;

        // El porcentaje/tope/moneda del premio solo aplican a WalletCredit; para los tipos no
        // monetarios se envían vacíos (la policy rechaza montos sobre un premio no monetario).
        var isWalletCredit = rewardType == ReferralRewardType.WalletCredit;
        return ReferralProgramPolicy.CreateTenantToTenant(
            input?.AttributionWindowDays ?? baseline.AttributionWindowDays,
            input?.MinimumPaymentAmountCents ?? baseline.MinimumPaymentAmountCents,
            input?.MinimumPaymentCurrency ?? baseline.MinimumPaymentCurrency,
            input?.WaitingPeriodDays ?? baseline.WaitingPeriodDays,
            input?.MaximumRewardsPerReferrerPerCalendarYear ?? baseline.MaximumRewardsPerReferrerPerCalendarYear,
            rewardType,
            input?.RewardDefinitionKey ?? baseline.RewardDefinitionKey,
            rewardBasisPoints: isWalletCredit ? input?.RewardBasisPoints ?? baseline.RewardBasisPoints : 0,
            rewardCapCents: isWalletCredit ? input?.RewardCapCents ?? baseline.RewardCapCents : null,
            rewardCurrency: isWalletCredit ? input?.RewardCurrency ?? baseline.RewardCurrency : null
        );
    }
}
