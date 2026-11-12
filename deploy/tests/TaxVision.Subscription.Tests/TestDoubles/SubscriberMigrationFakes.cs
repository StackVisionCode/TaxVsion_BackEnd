using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Migrations;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Uso de almacenamiento por oficina (null = CloudStorage no respondió).</summary>
public sealed class FakeStorageUsageClient(Dictionary<Guid, long?>? used = null) : IStorageUsageClient
{
    public Dictionary<Guid, long?> Used { get; } = used ?? [];
    public int Calls { get; private set; }

    public Task<long?> GetUsedBytesAsync(Guid tenantId, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Used.TryGetValue(tenantId, out var bytes) ? bytes : 0L);
    }
}

/// <summary>Asientos comprados y cuota de la foto actual por oficina.</summary>
public sealed class FakeSubscriberMigrationReads(Dictionary<Guid, OfficeHoldings>? holdings = null)
    : ISubscriberMigrationReads
{
    public Dictionary<Guid, OfficeHoldings> Holdings { get; } = holdings ?? [];

    public Task<IReadOnlyDictionary<Guid, OfficeHoldings>> GetHoldingsAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyDictionary<Guid, OfficeHoldings>>(
            tenantIds.ToDictionary(
                id => id,
                id => Holdings.TryGetValue(id, out var h) ? h : new OfficeHoldings(0, null)
            )
        );
}

public sealed class InMemorySubscriberMigrationRepo : ISubscriberMigrationRepository
{
    public List<SubscriberMigration> Migrations { get; } = [];

    public Task AddAsync(SubscriberMigration migration, CancellationToken ct = default)
    {
        Migrations.Add(migration);
        return Task.CompletedTask;
    }

    public Task<SubscriberMigration?> GetAsync(Guid migrationId, CancellationToken ct = default) =>
        Task.FromResult(Migrations.FirstOrDefault(m => m.Id == migrationId));

    public Task<SubscriberMigration?> GetForUpdateAsync(Guid migrationId, CancellationToken ct = default) =>
        GetAsync(migrationId, ct);

    public Task<IReadOnlyList<SubscriberMigration>> ListWithWorkAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SubscriberMigration>>(
            Migrations.Where(m => m.PendingTenantIds().Count > 0 || m.ScheduledTenantIds().Count > 0).ToList()
        );
}
