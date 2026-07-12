using System.Text.Json;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Common;

/// <summary>
/// Construye los eventos de integración con los límites efectivos
/// (plan + asientos extra). Único punto donde se calculan.
/// </summary>
public static class SubscriptionEventFactory
{
    public static SubscriptionActivatedIntegrationEvent Activated(
        TenantSubscription subscription,
        Plan plan,
        string correlationId
    ) =>
        new()
        {
            TenantId = subscription.TenantId,
            SubscribedTenantId = subscription.TenantId,
            PlanCode = plan.Code,
            MaxUsers = subscription.EffectiveMaxUsers(plan),
            MaxPendingInvitations = plan.MaxPendingInvitations,
            StorageQuotaBytes = plan.StorageQuotaBytes,
            EnabledModules = ParseModules(plan.EnabledModulesJson),
            TrialEndsAtUtc = subscription.TrialEndsAtUtc,
            CorrelationId = correlationId,
        };

    public static SubscriptionPlanChangedIntegrationEvent PlanChanged(
        TenantSubscription subscription,
        Plan plan,
        string correlationId
    ) =>
        new()
        {
            TenantId = subscription.TenantId,
            SubscribedTenantId = subscription.TenantId,
            PlanCode = plan.Code,
            MaxUsers = subscription.EffectiveMaxUsers(plan),
            MaxPendingInvitations = plan.MaxPendingInvitations,
            StorageQuotaBytes = plan.StorageQuotaBytes,
            EnabledModules = ParseModules(plan.EnabledModulesJson),
            CorrelationId = correlationId,
        };

    private static string[] ParseModules(string enabledModulesJson)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(enabledModulesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
