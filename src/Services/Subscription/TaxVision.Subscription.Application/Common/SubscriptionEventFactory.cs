using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Common;

/// <summary>
/// Construye los eventos de integración legacy que Auth y CloudStorage ya consumen
/// (SubscriptionActivated/PlanChanged), leyendo los límites efectivos desde los
/// entitlements/features de la versión de plan publicada. Único punto donde se traduce
/// el nuevo modelo de plan versionado al payload plano que esperan esos consumidores.
/// </summary>
public static class SubscriptionEventFactory
{
    public static SubscriptionActivatedIntegrationEvent Activated(
        TenantSubscription subscription,
        SubscriptionPlan plan,
        SubscriptionPlanVersion planVersion,
        string correlationId
    ) =>
        new()
        {
            TenantId = subscription.TenantId,
            SubscribedTenantId = subscription.TenantId,
            PlanCode = plan.Code.Value,
            MaxUsers = PlanVersionEntitlements.GetInt(planVersion, "seats.max", fallback: 0),
            MaxPendingInvitations = PlanVersionEntitlements.GetInt(planVersion, "invitations.max_pending", fallback: 0),
            StorageQuotaBytes = PlanVersionEntitlements.GetLong(planVersion, "storage.max_bytes", fallback: 0),
            EnabledModules = PlanVersionEntitlements.GetEnabledModules(planVersion),
            TrialEndsAtUtc = subscription.TrialEndsAtUtc,
            CorrelationId = correlationId,
        };

    public static SubscriptionPlanChangedIntegrationEvent PlanChanged(
        TenantSubscription subscription,
        SubscriptionPlan plan,
        SubscriptionPlanVersion planVersion,
        string correlationId
    ) =>
        new()
        {
            TenantId = subscription.TenantId,
            SubscribedTenantId = subscription.TenantId,
            PlanCode = plan.Code.Value,
            MaxUsers = PlanVersionEntitlements.GetInt(planVersion, "seats.max", fallback: 0),
            MaxPendingInvitations = PlanVersionEntitlements.GetInt(planVersion, "invitations.max_pending", fallback: 0),
            StorageQuotaBytes = PlanVersionEntitlements.GetLong(planVersion, "storage.max_bytes", fallback: 0),
            EnabledModules = PlanVersionEntitlements.GetEnabledModules(planVersion),
            CorrelationId = correlationId,
        };
}
