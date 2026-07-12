namespace TaxVision.Subscription.Application.Entitlements;

public static class EntitlementCacheKeys
{
    public static string Summary(Guid tenantId) => $"subscription:entitlements:{tenantId}";
}
