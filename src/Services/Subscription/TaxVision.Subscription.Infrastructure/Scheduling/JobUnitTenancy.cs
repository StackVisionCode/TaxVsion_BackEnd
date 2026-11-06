using BuildingBlocks.Jobs;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

internal static class JobUnitTenancy
{
    /// <summary>Los jobs cruzan tenants: cada unidad fija el suyo antes de leer (filtro fail-closed del DbContext).</summary>
    public static void UseTenant(this JobUnitContext<SubscriptionDbContext> unit, Guid tenantId) =>
        unit.Services.GetRequiredService<ITenantContext>().SetTenant(tenantId);
}
