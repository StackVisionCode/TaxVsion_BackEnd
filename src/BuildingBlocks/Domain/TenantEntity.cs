namespace BuildingBlocks.Domain;

public abstract class TenantEntity : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;
}
