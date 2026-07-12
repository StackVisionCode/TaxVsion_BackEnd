namespace BuildingBlocks.Tenancy;

/// <summary>
/// Implementación concreta mutable del <see cref="ITenantContext"/>. Registrada como
/// Scoped en <c>AddBuildingBlocks</c> — el middleware/consumer setea el tenant al
/// inicio del scope y todos los servicios que lo inyectan ven el mismo valor.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set");

    public bool HasTenant => _tenantId.HasValue;

    public void SetTenant(Guid tenantId) => _tenantId = tenantId;
}
