using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Application.Rewards;

namespace TaxVision.Growth.Api.IntegrationEvents;

/// <summary>Los eventos de aviso para los casos de uso que los devuelven en cascada.</summary>
public sealed class ReferralNoticeEvents : IReferralNoticeEvents
{
    public object Attributed(CreateReferralAttributionResult attribution, string correlationId) =>
        new ReferralAttributedIntegrationEvent
        {
            TenantId = attribution.ReferrerTenantId,
            CorrelationId = correlationId,
            AggregateId = attribution.AttributionId,
            AttributionId = attribution.AttributionId,
            ReferrerUserId = attribution.ReferrerUserId,
            Channel = attribution.Channel,
        };

    public object Qualified(ReferralRewardNotice reward, Guid paymentId, string correlationId) =>
        ReferralRewardEvents.Qualified(reward, paymentId, correlationId);

    public object Rejected(ReferralRewardNotice reward, string reasonCode, string correlationId) =>
        ReferralRewardEvents.Rejected(reward, reasonCode, correlationId);
}
