using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;

public sealed record CreateReferralAttributionCommand(
    Guid TenantId,
    Guid ProgramId,
    string ReferralCode,
    ReferralParticipantType RefereeType,
    Guid RefereeId,
    string IdempotencyKey,
    Guid ActorUserId,
    ReferralShareChannel? Channel = null,
    Guid? ShareId = null
)
{
    public override string ToString() =>
        $"{nameof(CreateReferralAttributionCommand)} {{ TenantId = {TenantId}, "
        + $"ProgramId = {ProgramId}, ReferralCode = <redacted>, RefereeType = {RefereeType}, "
        + $"RefereeId = {RefereeId} }}";
}
