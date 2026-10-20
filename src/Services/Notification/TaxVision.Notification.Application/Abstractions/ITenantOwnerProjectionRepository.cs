using TaxVision.Notification.Domain.Recipients;

namespace TaxVision.Notification.Application.Abstractions;

public interface ITenantOwnerProjectionRepository
{
    /// <summary>El owner activo del tenant, si está proyectado. <c>null</c> = no proyectado (no existe).</summary>
    Task<Guid?> GetActiveOwnerUserIdAsync(Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetActiveAdminUserIdsAsync(Guid tenantId, CancellationToken ct = default);

    Task<TenantOwnerProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task AddAsync(TenantOwnerProjection projection, CancellationToken ct = default);

    Task<TenantOwnerProjection?> GetByUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
