using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Rewards;

/// <summary>
/// Instrucción interna para que el host la convierta en outbox command. No concede
/// beneficios directamente y no contiene balance ni valor monetario.
/// </summary>
public sealed record ReferralRewardInstruction(
    Guid RewardCaseId,
    Guid AttemptId,
    Guid GrantId,
    ReferralRewardOperation Operation,
    ReferralParticipantType BeneficiaryType,
    Guid BeneficiaryId,
    ReferralRewardType RewardType,
    string RewardDefinitionKey
);
