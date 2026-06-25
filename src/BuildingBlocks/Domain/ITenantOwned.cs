namespace BuildingBlocks.Domain;

public interface ITenantOwned
{
    Guid TenantId { get; }
    void SetTenant(Guid tenantId);
}
