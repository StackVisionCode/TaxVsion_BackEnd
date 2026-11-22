using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Recipients;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Owner y admins activos de la oficina (sin owner: nadie).</summary>
internal sealed class FixedOffices(Guid? owner = null, params Guid[] admins) : ITenantOwnerProjectionRepository
{
    public Task<Guid?> GetActiveOwnerUserIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(owner);

    public Task<IReadOnlyList<Guid>> GetActiveAdminUserIdsAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(admins);

    public Task<TenantOwnerProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult<TenantOwnerProjection?>(null);

    public Task AddAsync(TenantOwnerProjection projection, CancellationToken ct = default) => Task.CompletedTask;

    public Task<TenantOwnerProjection?> GetByUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult<TenantOwnerProjection?>(null);
}
