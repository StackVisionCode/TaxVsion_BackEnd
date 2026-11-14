using TaxVision.FinancialInsights.Domain.Projections;

namespace TaxVision.FinancialInsights.Application.Projections.Abstractions;

/// <summary>Proyecciones locales para consumers y reparación; siempre con tenant explícito.</summary>
public interface IFinancialProjectionRepository
{
    Task<UserPermissionsProjection?> FindUserPermissionsAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    void AddUserPermissions(UserPermissionsProjection projection);

    Task<RolePermissionsProjection?> FindRolePermissionsAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);

    void AddRolePermissions(RolePermissionsProjection projection);

    Task<TenantEntitlementsProjection?> FindTenantEntitlementsAsync(Guid tenantId, CancellationToken ct = default);

    void AddTenantEntitlements(TenantEntitlementsProjection projection);

    /// <summary>Guarda una fila traída por pull. <c>false</c> si un consumer la creó al mismo tiempo.</summary>
    Task<bool> TryAddTenantEntitlementsAsync(TenantEntitlementsProjection projection, CancellationToken ct = default);

    Task<TenantDirectoryProjection?> FindTenantDirectoryAsync(Guid tenantId, CancellationToken ct = default);

    void AddTenantDirectory(TenantDirectoryProjection projection);

    Task<CustomerDirectoryProjection?> FindCustomerAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<IReadOnlyCollection<Guid>> FindKnownCustomerIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken ct = default
    );

    void AddCustomer(CustomerDirectoryProjection projection);
}
