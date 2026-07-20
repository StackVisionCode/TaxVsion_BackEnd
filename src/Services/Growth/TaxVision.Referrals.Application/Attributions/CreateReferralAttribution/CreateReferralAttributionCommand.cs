using TaxVision.Referrals.Domain.Participants;

namespace TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;

public sealed record CreateReferralAttributionCommand(
    Guid TenantId,
    Guid ProgramId,
    string ReferralCode,
    ReferralParticipantType RefereeType,
    Guid RefereeId,
    string IdempotencyKey,
    Guid ActorUserId
)
{
    public override string ToString() =>
        $"{nameof(CreateReferralAttributionCommand)} {{ TenantId = {TenantId}, "
        + $"ProgramId = {ProgramId}, ReferralCode = <redacted>, RefereeType = {RefereeType}, "
        + $"RefereeId = {RefereeId} }}";
}
