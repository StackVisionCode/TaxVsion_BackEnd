using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class CatalogRevisionStore(SubscriptionDbContext db) : ICatalogRevisionStore
{
    public Task<bool> IsAppliedAsync(string revisionId, CancellationToken ct = default) =>
        db.CatalogRevisions.AsNoTracking().AnyAsync(revision => revision.RevisionId == revisionId, ct);

    public async Task AddAsync(AppliedCatalogRevision revision, CancellationToken ct = default) =>
        await db.CatalogRevisions.AddAsync(revision, ct);

    public async Task<IReadOnlyList<AppliedCatalogRevision>> GetPendingFollowUpsAsync(CancellationToken ct = default) =>
        await db
            .CatalogRevisions.Where(revision => revision.FollowUpPublishedAtUtc == null)
            .OrderBy(revision => revision.AppliedAtUtc)
            .ToListAsync(ct);
}

/// <summary>Cruza tenants a propósito: solo lo usan las revisiones del catálogo al desplegar.</summary>
public sealed class PlanSubscriptionsAcrossTenants(SubscriptionDbContext db) : IPlanSubscriptionsAcrossTenants
{
    public async Task<IReadOnlyList<TenantSubscription>> ListByPlanAcrossTenantsAsync(
        Guid planId,
        CancellationToken ct = default
    ) =>
        await db
            .Subscriptions.IgnoreQueryFilters()
            // Cambios de plan en curso: la vista previa y el job de migraciones los miran.
            .Include(subscription => subscription.PendingDowngrades)
            .Include(subscription => subscription.PlanChangeRequests)
            .Where(subscription => subscription.PlanId == planId)
            .OrderBy(subscription => subscription.TenantId)
            .ToListAsync(ct);
}

/// <summary>Solo agrega y consulta: la auditoría del catálogo nunca se edita ni se borra.</summary>
public sealed class CatalogAuditLog(SubscriptionDbContext db) : ICatalogAuditLog
{
    public async Task AddAsync(CatalogAuditEntry entry, CancellationToken ct = default) =>
        await db.CatalogAuditEntries.AddAsync(entry, ct);

    public async Task<IReadOnlyList<CatalogAuditEntry>> ListAsync(
        CatalogAuditFilter filter,
        CancellationToken ct = default
    )
    {
        var query = db.CatalogAuditEntries.AsNoTracking();
        if (filter.EntityType is not null)
            query = query.Where(entry => entry.EntityType == filter.EntityType);
        if (filter.EntityId is not null)
            query = query.Where(entry => entry.EntityId == filter.EntityId);
        if (filter.BeforeUtc is not null)
            query = query.Where(entry => entry.OccurredAtUtc < filter.BeforeUtc);

        return await query.OrderByDescending(entry => entry.OccurredAtUtc).Take(filter.Take).ToListAsync(ct);
    }
}
