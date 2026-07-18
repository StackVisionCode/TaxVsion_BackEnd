using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Backfill;

namespace TaxVision.Correspondence.Tests.Backfill;

internal sealed class FakeTenantBackfillStateRepository : ITenantBackfillStateRepository
{
    private readonly List<TenantBackfillState> _store = [];

    public IReadOnlyList<TenantBackfillState> All => _store;

    public Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(_store.FirstOrDefault(x => x.TenantId == tenantId));

    public Task AddAsync(TenantBackfillState entity, CancellationToken ct = default)
    {
        _store.Add(entity);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListAllTenantIdsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(_store.Select(x => x.TenantId).ToList());
}
