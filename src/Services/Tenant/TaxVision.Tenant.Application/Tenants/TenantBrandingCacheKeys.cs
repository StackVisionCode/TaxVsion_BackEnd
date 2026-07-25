namespace TaxVision.Tenant.Application.Tenants;

public static class TenantBrandingCacheKeys
{
    public static string Colors(Guid tenantId) => $"tenant:branding:colors:{tenantId}";
}
