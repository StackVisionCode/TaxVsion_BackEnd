using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Rendering;

internal sealed class FakeTenantProfileRefRepository : ITenantProfileRefRepository
{
    public Dictionary<Guid, string> Names { get; } = [];

    public Task<TenantProfileRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(
            Names.TryGetValue(tenantId, out var name)
                ? TenantProfileRef.Create(tenantId, name, "oficina", DateTime.UtcNow)
                : null
        );

    public Task UpsertAsync(
        Guid tenantId,
        string name,
        string subDomain,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        Names[tenantId] = name;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<Guid>> GetKnownTenantIdsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyCollection<Guid>>(Names.Keys.ToList());
}
