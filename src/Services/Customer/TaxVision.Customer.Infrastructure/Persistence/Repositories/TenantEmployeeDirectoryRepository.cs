using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Employees;

namespace TaxVision.Customer.Infrastructure.Persistence.Repositories;

public sealed class TenantEmployeeDirectoryRepository(CustomerDbContext db)
    : ITenantEmployeeDirectoryRepository,
        ITenantEmployeeDirectoryReconciliationRepository
{
    public async Task<IReadOnlyDictionary<Guid, TenantEmployeeDirectoryEntry>> GetByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct = default
    ) =>
        // Lectura cross-tenant de la reconciliación: explícito por si la entidad pasa a ser ITenantOwned.
        await db
            .TenantEmployeeDirectoryEntries.IgnoreQueryFilters()
            .Where(e => userIds.Contains(e.UserId))
            .ToDictionaryAsync(e => e.UserId, ct);

    public void Add(TenantEmployeeDirectoryEntry entry) => db.TenantEmployeeDirectoryEntries.Add(entry);

    public Task<TenantEmployeeDirectoryEntry?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        db.TenantEmployeeDirectoryEntries.FirstOrDefaultAsync(e => e.UserId == userId, ct);

    public async Task UpsertAsync(
        Guid userId,
        Guid tenantId,
        string actorType,
        bool isActive,
        CancellationToken ct = default
    )
    {
        var existing = await db.TenantEmployeeDirectoryEntries.FirstOrDefaultAsync(e => e.UserId == userId, ct);
        if (existing is null)
        {
            await db.TenantEmployeeDirectoryEntries.AddAsync(
                TenantEmployeeDirectoryEntry.Create(userId, tenantId, actorType, isActive),
                ct
            );
            return;
        }

        existing.UpdateActorType(actorType);
        if (isActive)
            existing.MarkActive();
        else
            existing.MarkInactive();
    }

    public async Task MarkActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var existing = await db.TenantEmployeeDirectoryEntries.FirstOrDefaultAsync(e => e.UserId == userId, ct);
        existing?.MarkActive();
    }

    public async Task MarkInactiveAsync(Guid userId, CancellationToken ct = default)
    {
        var existing = await db.TenantEmployeeDirectoryEntries.FirstOrDefaultAsync(e => e.UserId == userId, ct);
        existing?.MarkInactive();
    }

    public async Task MarkOffboardedAsync(Guid userId, CancellationToken ct = default)
    {
        var existing = await db.TenantEmployeeDirectoryEntries.FirstOrDefaultAsync(e => e.UserId == userId, ct);
        existing?.MarkOffboarded();
    }
}
