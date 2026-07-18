using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Projections;

internal sealed class FakeTenantLogoRefRepository : ITenantLogoRefRepository
{
    private readonly Dictionary<Guid, TenantLogoRef> _store = new();

    public void Seed(TenantLogoRef logoRef) => _store[logoRef.TenantId] = logoRef;

    public Task<TenantLogoRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(tenantId, out var value) ? value : null);

    public Task AddAsync(TenantLogoRef logoRef, CancellationToken ct = default)
    {
        _store[logoRef.TenantId] = logoRef;
        return Task.CompletedTask;
    }
}
