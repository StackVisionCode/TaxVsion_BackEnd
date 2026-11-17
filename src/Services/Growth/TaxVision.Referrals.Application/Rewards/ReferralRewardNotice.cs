using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Rewards;

/// <summary>Lo que el referidor necesita saber de su premio para avisarle (tenant = la oficina referidora).</summary>
public sealed record ReferralRewardNotice(
    Guid TenantId,
    Guid RewardCaseId,
    Guid AttributionId,
    Guid QualificationId,
    Guid GrantId,
    long AmountCents,
    string? Currency,
    DateTime EligibleAtUtc
)
{
    public static ReferralRewardNotice From(ReferralRewardCase reward) =>
        new(
            reward.TenantId,
            reward.Id,
            reward.AttributionId,
            reward.QualificationId,
            reward.GrantId,
            reward.RewardAmountCents,
            reward.RewardCurrency,
            reward.EligibleAtUtc
        );
}
