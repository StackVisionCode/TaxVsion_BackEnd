using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Settings;
using DomainIssuerProfile = TaxVision.Billing.Domain.Invoices.IssuerProfile;

namespace TaxVision.Billing.Tests.Fakes;

/// <summary>Perfiles de emisor en memoria (uno por tenant), como el repo real con IgnoreQueryFilters.</summary>
internal sealed class InMemoryIssuerProfileRepository : IIssuerProfileRepository
{
    public List<DomainIssuerProfile> Rows { get; } = [];

    public Task<DomainIssuerProfile?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(p => p.TenantId == tenantId));

    public Task AddAsync(DomainIssuerProfile profile, CancellationToken ct = default)
    {
        Rows.Add(profile);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<DomainIssuerProfile> Items, int TotalCount)> ListPageAsync(
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<DomainIssuerProfile> items = Rows.OrderBy(p => p.TenantId)
            .Skip((page - 1) * size)
            .Take(size)
            .ToList();
        return Task.FromResult((items, Rows.Count));
    }
}

internal sealed class InMemoryTenantBillingSettingsRepository : ITenantBillingSettingsRepository
{
    public List<TenantBillingSettings> Rows { get; } = [];

    public Task<TenantBillingSettings?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(s => s.TenantId == tenantId));

    public Task AddAsync(TenantBillingSettings settings, CancellationToken ct = default)
    {
        Rows.Add(settings);
        return Task.CompletedTask;
    }
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Saves++;
        return Task.FromResult(1);
    }
}

internal sealed class NoopCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = string.Empty;

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        CorrelationId = correlationId;
        return new Noop();
    }

    private sealed class Noop : IDisposable
    {
        public void Dispose() { }
    }
}

internal sealed class FixedClock(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
