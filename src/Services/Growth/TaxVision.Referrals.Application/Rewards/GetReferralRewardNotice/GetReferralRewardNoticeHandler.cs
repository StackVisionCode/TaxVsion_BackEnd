using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Referrals.Application.Rewards.GetReferralRewardNotice;

/// <summary>El resumen del premio de una oficina, para avisarle; null si no existe.</summary>
public sealed record GetReferralRewardNoticeQuery(Guid TenantId, Guid RewardCaseId);

public static class GetReferralRewardNoticeHandler
{
    public static async Task<Result<ReferralRewardNotice?>> Handle(
        GetReferralRewardNoticeQuery query,
        IReferralRewardCaseRepository rewards,
        CancellationToken ct
    )
    {
        var reward = await rewards.GetByIdAsync(query.RewardCaseId, query.TenantId, ct);
        return Result.Success(reward is null ? null : ReferralRewardNotice.From(reward));
    }
}
