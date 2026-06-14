namespace BuildingBlocks.Domain;

public interface ITenantOwned
{
    Guid TenantId { get; }
    void SetTenantId(Guid tenantId);
}
