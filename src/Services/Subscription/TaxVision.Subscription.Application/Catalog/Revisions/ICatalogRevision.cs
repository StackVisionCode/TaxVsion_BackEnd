using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Catalog;

namespace TaxVision.Subscription.Application.Catalog.Revisions;

/// <summary>
/// Un cambio del catálogo que viaja en el código y se aplica una sola vez al desplegar (D3). Todo cambio pasa por
/// los agregados; el runner guarda, registra la revisión y confirma en una transacción.
/// </summary>
public interface ICatalogRevision
{
    /// <summary>Fecha y nombre, por ejemplo <c>2026-11-11-r1-rebaseline</c>. Nunca se reutiliza.</summary>
    string Id { get; }

    string Description { get; }

    /// <summary>Aplica la revisión sobre agregados rastreados y devuelve un resumen para el registro.</summary>
    Task<Result<string>> ApplyAsync(CatalogRevisionContext context, CancellationToken ct);
}

public sealed record CatalogRevisionContext(
    IPlanRepository Plans,
    IPlanSubscriptionsAcrossTenants Subscriptions,
    SubscriptionOptions Options,
    ICatalogAuditLog CatalogAudit,
    ISubscriptionAuditLogWriter TenantAudit,
    CatalogAuditActor Actor,
    DateTime NowUtc
)
{
    /// <summary>Actor de los cambios hechos al desplegar.</summary>
    public static readonly Guid DeployActor = Guid.Empty;

    /// <summary>Correlación de todo lo que hace la revisión: <c>deploy:{id}</c>.</summary>
    public string CorrelationId => Actor.Label;
}

/// <summary>Las revisiones en el orden en que se aplican. Agregar al final; nunca reordenar ni borrar.</summary>
public static class CatalogRevisionCatalog
{
    public static IReadOnlyList<ICatalogRevision> All { get; } =
    [new R1RebaselineRevision(), new R2SingleEnterpriseRevision(), new R3PlanDescriptionsRevision()];
}
