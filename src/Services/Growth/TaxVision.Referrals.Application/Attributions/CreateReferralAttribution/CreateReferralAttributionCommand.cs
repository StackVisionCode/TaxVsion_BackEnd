using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;

/// <param name="NoticeCorrelationId">
/// Con valor, el caso de uso devuelve el aviso al referidor con esta correlación (alta por evento). Sin valor no
/// hay aviso, como en el alta manual.
/// </param>
public sealed record CreateReferralAttributionCommand(
    Guid TenantId,
    Guid ProgramId,
    string ReferralCode,
    ReferralParticipantType RefereeType,
    Guid RefereeId,
    string IdempotencyKey,
    Guid ActorUserId,
    ReferralShareChannel? Channel = null,
    Guid? ShareId = null,
    string? NoticeCorrelationId = null
)
{
    public override string ToString() =>
        $"{nameof(CreateReferralAttributionCommand)} {{ TenantId = {TenantId}, "
        + $"ProgramId = {ProgramId}, ReferralCode = <redacted>, RefereeType = {RefereeType}, "
        + $"RefereeId = {RefereeId} }}";
}
