using BuildingBlocks.Tenancy;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Tenant de prueba que los jobs fijan por unidad, como el TenantContext de la flota.</summary>
public sealed class SettableTenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public bool HasTenant => TenantId != Guid.Empty;

    public void SetTenant(Guid tenantId) => TenantId = tenantId;
}
