using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Abstractions;

/// <summary>
/// Suscripciones de todas las oficinas de un plan, rastreadas. Cruza tenants a propósito: solo lo usan las revisiones
/// del catálogo y las vistas previas de operaciones peligrosas del admin de plataforma.
/// </summary>
public interface IPlanSubscriptionsAcrossTenants
{
    Task<IReadOnlyList<TenantSubscription>> ListByPlanAcrossTenantsAsync(Guid planId, CancellationToken ct = default);
}

/// <summary>Registro de las revisiones del catálogo ya aplicadas.</summary>
public interface ICatalogRevisionStore
{
    Task<bool> IsAppliedAsync(string revisionId, CancellationToken ct = default);

    Task AddAsync(AppliedCatalogRevision revision, CancellationToken ct = default);

    /// <summary>Aplicadas cuyos avisos todavía no salieron, por orden de aplicación (rastreadas).</summary>
    Task<IReadOnlyList<AppliedCatalogRevision>> GetPendingFollowUpsAsync(CancellationToken ct = default);
}
