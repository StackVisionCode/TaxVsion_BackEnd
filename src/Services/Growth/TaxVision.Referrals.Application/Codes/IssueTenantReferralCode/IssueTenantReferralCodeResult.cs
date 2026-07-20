using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Participants;

namespace TaxVision.Referrals.Application.Codes.IssueTenantReferralCode;

/// <summary>
/// Replay-safe persisted response. It deliberately contains display metadata only;
/// the clear-text referral token is regenerated at the delivery boundary.
/// </summary>
public sealed record IssueTenantReferralCodeResult(
    Guid ReferralCodeId,
    Guid ProgramId,
    ReferralParticipantType OwnerType,
    Guid OwnerId,
    ReferralCodeStatus Status,
    string DisplayPrefix,
    string LastFour,
    DateTime ExpiresAtUtc
)
{
    public static IssueTenantReferralCodeResult From(ReferralCode code) =>
        new(
            code.Id,
            code.ProgramId,
            code.OwnerType,
            code.OwnerId,
            code.Status,
            code.DisplayPrefix,
            code.LastFour,
            code.ExpiresAtUtc
        );
}
