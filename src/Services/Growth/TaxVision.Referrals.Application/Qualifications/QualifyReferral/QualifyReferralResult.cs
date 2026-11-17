using TaxVision.Referrals.Domain.Qualifications;

namespace TaxVision.Referrals.Application.Qualifications.QualifyReferral;

public sealed record QualifyReferralResult(
    Guid QualificationId,
    ReferralQualificationDecision Decision,
    string? RejectionReasonCode,
    Guid? RewardCaseId,
    bool WasReplay,
    // El premio creado al calificar, para avisarle al referidor; null si no hubo premio.
    Rewards.ReferralRewardNotice? Reward = null
);
