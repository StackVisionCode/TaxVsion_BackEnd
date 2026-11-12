using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Suscripciones de varias oficinas en memoria, para revisiones y vistas previas del catálogo.</summary>
public sealed class InMemoryPlanSubscriptions(params TenantSubscription[] subscriptions)
    : IPlanSubscriptionsAcrossTenants
{
    private readonly List<TenantSubscription> _subscriptions = [.. subscriptions];

    public void Add(TenantSubscription subscription) => _subscriptions.Add(subscription);

    public Task<IReadOnlyList<TenantSubscription>> ListByPlanAcrossTenantsAsync(
        Guid planId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<TenantSubscription>>(_subscriptions.Where(s => s.PlanId == planId).ToList());
}

/// <summary>Registro de revisiones en memoria.</summary>
public sealed class InMemoryCatalogRevisionStore(params AppliedCatalogRevision[] applied) : ICatalogRevisionStore
{
    public List<AppliedCatalogRevision> Rows { get; } = [.. applied];

    public Task<bool> IsAppliedAsync(string revisionId, CancellationToken ct = default) =>
        Task.FromResult(Rows.Any(row => row.RevisionId == revisionId));

    public Task AddAsync(AppliedCatalogRevision revision, CancellationToken ct = default)
    {
        Rows.Add(revision);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AppliedCatalogRevision>> GetPendingFollowUpsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AppliedCatalogRevision>>(
            Rows.Where(row => row.FollowUpPublishedAtUtc is null).OrderBy(row => row.AppliedAtUtc).ToList()
        );
}

/// <summary>Auditoría de catálogo en memoria, con la misma consulta que la real (más nuevas primero).</summary>
public sealed class InMemoryCatalogAuditLog : ICatalogAuditLog
{
    public List<CatalogAuditEntry> Entries { get; } = [];

    public Task AddAsync(CatalogAuditEntry entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CatalogAuditEntry>> ListAsync(
        CatalogAuditFilter filter,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<CatalogAuditEntry>>(
            Entries
                .Where(entry => filter.EntityType is null || entry.EntityType == filter.EntityType)
                .Where(entry => filter.EntityId is null || entry.EntityId == filter.EntityId)
                .Where(entry => filter.BeforeUtc is null || entry.OccurredAtUtc < filter.BeforeUtc)
                .OrderByDescending(entry => entry.OccurredAtUtc)
                .Take(filter.Take)
                .ToList()
        );
}
