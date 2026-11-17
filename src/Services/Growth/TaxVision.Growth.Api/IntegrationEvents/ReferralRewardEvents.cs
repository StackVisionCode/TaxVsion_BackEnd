using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using TaxVision.Referrals.Application.Rewards;

namespace TaxVision.Growth.Api.IntegrationEvents;

/// <summary>
/// Los avisos del premio al referidor. Cada evento lleva un id estable por premio y momento: una reentrega del mensaje
/// que lo originó publica el mismo id y Notification no repite el aviso.
/// </summary>
public static class ReferralRewardEvents
{
    public static ReferralQualifiedIntegrationEvent Qualified(
        ReferralRewardNotice reward,
        Guid paymentId,
        string correlationId
    ) =>
        new()
        {
            EventId = StableId("qualified", reward.RewardCaseId),
            TenantId = reward.TenantId,
            CorrelationId = correlationId,
            AggregateId = reward.RewardCaseId,
            AttributionId = reward.AttributionId,
            QualificationId = reward.QualificationId,
            ProgramType = "TenantToTenant",
            QualifyingPaymentId = paymentId,
            RewardAmountCents = reward.AmountCents,
            RewardCurrency = reward.Currency,
            RewardAvailableAtUtc = reward.EligibleAtUtc,
        };

    public static ReferralRewardGrantedIntegrationEvent Granted(
        ReferralRewardNotice reward,
        Guid ledgerEntryId,
        string correlationId
    ) =>
        new()
        {
            EventId = StableId("granted", reward.RewardCaseId),
            TenantId = reward.TenantId,
            CorrelationId = correlationId,
            AggregateId = reward.RewardCaseId,
            RewardCaseId = reward.RewardCaseId,
            GrantId = reward.GrantId,
            BenefitType = "WalletCredit",
            MaterializedBy = "Wallet",
            MaterializedReferenceId = ledgerEntryId,
            RewardAmountCents = reward.AmountCents,
            RewardCurrency = reward.Currency,
        };

    public static ReferralRewardRejectedIntegrationEvent Rejected(
        ReferralRewardNotice reward,
        string reasonCode,
        string correlationId
    ) =>
        new()
        {
            EventId = StableId("rejected", reward.RewardCaseId),
            TenantId = reward.TenantId,
            CorrelationId = correlationId,
            AggregateId = reward.RewardCaseId,
            RewardCaseId = reward.RewardCaseId,
            GrantId = reward.GrantId,
            ReasonCode = reasonCode,
            RewardAmountCents = reward.AmountCents,
            RewardCurrency = reward.Currency,
        };

    public static ReferralRewardReversedIntegrationEvent Reversed(
        ReferralRewardNotice reward,
        Guid sourceEventId,
        string correlationId
    ) =>
        new()
        {
            EventId = StableId("reversed", reward.RewardCaseId),
            TenantId = reward.TenantId,
            CorrelationId = correlationId,
            AggregateId = reward.RewardCaseId,
            RewardCaseId = reward.RewardCaseId,
            GrantId = reward.GrantId,
            SourceFinancialEventId = sourceEventId,
            ReasonCode = "ReferredPaymentReversed",
            RewardAmountCents = reward.AmountCents,
            RewardCurrency = reward.Currency,
        };

    // UUID versión 5 (por nombre) con variante RFC 4122: Communication rechaza ids que no lo sean.
    private static Guid StableId(string moment, Guid rewardCaseId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"growth.referral-reward:{moment}:{rewardCaseId:N}"))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
