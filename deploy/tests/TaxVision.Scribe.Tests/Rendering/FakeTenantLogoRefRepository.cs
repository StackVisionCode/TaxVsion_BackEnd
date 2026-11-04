using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Rendering;

internal sealed class FakeTenantLogoRefRepository : ITenantLogoRefRepository
{
    private readonly Dictionary<(Guid, TenantLogoSurface), TenantLogoRef> _store = new();

    public void Seed(TenantLogoRef logoRef) => _store[(logoRef.TenantId, logoRef.Surface)] = logoRef;

    public Task<TenantLogoRef?> GetAsync(Guid tenantId, TenantLogoSurface surface, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue((tenantId, surface), out var value) ? value : null);

    public Task<IReadOnlyList<TenantLogoRef>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TenantLogoRef>>(_store.Values.Where(r => r.TenantId == tenantId).ToList());

    public Task AddAsync(TenantLogoRef logoRef, CancellationToken ct = default)
    {
        _store[(logoRef.TenantId, logoRef.Surface)] = logoRef;
        return Task.CompletedTask;
    }
}
