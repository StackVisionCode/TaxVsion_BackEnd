using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Rendering;

internal sealed class FakeTenantProfileRefRepository : ITenantProfileRefRepository
{
    public Dictionary<Guid, string> Names { get; } = [];

    // Perfil de la oficina (nombre comercial, dirección postal) para las oficinas de Names.
    public Dictionary<Guid, (string? DisplayName, string? PostalAddress)> Profiles { get; } = [];

    public Task<TenantProfileRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!Names.TryGetValue(tenantId, out var name))
            return Task.FromResult<TenantProfileRef?>(null);

        var row = TenantProfileRef.Create(tenantId, name, "oficina", DateTime.UtcNow);
        if (Profiles.TryGetValue(tenantId, out var profile))
            row.ApplyProfile(1, profile.DisplayName, profile.PostalAddress, DateTime.UtcNow);
        return Task.FromResult<TenantProfileRef?>(row);
    }

    public Task AddIfMissingAsync(
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
