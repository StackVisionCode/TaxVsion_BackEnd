using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Migrations;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SubscriberMigrationRepository(SubscriptionDbContext db) : ISubscriberMigrationRepository
{
    public async Task AddAsync(SubscriberMigration migration, CancellationToken ct = default) =>
        await db.SubscriberMigrations.AddAsync(migration, ct);

    public Task<SubscriberMigration?> GetAsync(Guid migrationId, CancellationToken ct = default) =>
        db
            .SubscriberMigrations.AsNoTracking()
            .Include(migration => migration.Offices)
            .FirstOrDefaultAsync(migration => migration.Id == migrationId, ct);

    public Task<SubscriberMigration?> GetForUpdateAsync(Guid migrationId, CancellationToken ct = default) =>
        db
            .SubscriberMigrations.Include(migration => migration.Offices)
            .FirstOrDefaultAsync(migration => migration.Id == migrationId, ct);

    public async Task<IReadOnlyList<SubscriberMigration>> ListWithWorkAsync(CancellationToken ct = default) =>
        await db
            .SubscriberMigrations.AsNoTracking()
            .Include(migration => migration.Offices)
            .Where(migration =>
                (
                    migration.Status == SubscriberMigrationStatus.Running
                    && migration.Offices.Any(office => office.Outcome == SubscriberMigrationOutcome.Pending)
                )
                || (
                    migration.Status == SubscriberMigrationStatus.Cancelled
                    && migration.Offices.Any(office => office.Outcome == SubscriberMigrationOutcome.Scheduled)
                )
            )
            .OrderBy(migration => migration.CreatedAtUtc)
            .ToListAsync(ct);
}

/// <summary>Cruza tenants a propósito: solo lo usan la vista previa y el job de migraciones del admin de plataforma.</summary>
public sealed class SubscriberMigrationReads(SubscriptionDbContext db) : ISubscriberMigrationReads
{
    public async Task<IReadOnlyDictionary<Guid, OfficeHoldings>> GetHoldingsAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken ct = default
    )
    {
        var ids = tenantIds.Distinct().ToList();
        var seats = await db
            .Seats.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(seat =>
                ids.Contains(seat.TenantId) && seat.Status != SeatStatus.Cancelled && seat.Status != SeatStatus.Expired
            )
            .GroupBy(seat => seat.TenantId)
            .Select(group => new { TenantId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.TenantId, row => row.Count, ct);
        var snapshots = await db
            .EntitlementSnapshots.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(snapshot => ids.Contains(snapshot.TenantId))
            .ToListAsync(ct);

        return ids.ToDictionary(
            id => id,
            id =>
            {
                var storage = snapshots
                    .FirstOrDefault(snapshot => snapshot.TenantId == id)
                    ?.Entries.FirstOrDefault(entry => entry.Key.Value == "storage.max_bytes")
                    ?.Value;
                return new OfficeHoldings(
                    seats.GetValueOrDefault(id),
                    long.TryParse(storage, out var bytes) ? bytes : null
                );
            }
        );
    }
}
