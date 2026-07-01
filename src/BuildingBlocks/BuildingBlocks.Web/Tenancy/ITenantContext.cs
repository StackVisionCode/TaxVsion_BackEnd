namespace BuildingBlocks.Tenancy;

public interface ITenantContext
{
    Guid TenantId { get; }
    bool HasTenant { get; }
}

public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public Guid TenantId =>
    _tenantId ?? throw new InvalidOperationException("TenantId is not set");
    public bool HasTenant => _tenantId.HasValue;

    public void SetTenant(Guid tenantId) => _tenantId = tenantId;

}
