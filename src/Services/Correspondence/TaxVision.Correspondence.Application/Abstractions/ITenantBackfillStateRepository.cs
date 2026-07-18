using TaxVision.Correspondence.Domain.Backfill;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>Marca de "backfill de CustomerEmailAddresses ya corrido" por tenant (Fase 2).</summary>
public interface ITenantBackfillStateRepository
{
    Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(TenantBackfillState entity, CancellationToken ct = default);

    /// <summary>
    /// Fase 16 — universo de tenants que <c>CustomerEmailReconciliationJob</c> (plan §32 R1) debe
    /// recorrer: un tenant que todavía no completó el backfill inicial no tiene proyección local
    /// que reconciliar, así que no tiene sentido incluirlo (el backfill mismo se encarga de
    /// poblarlo por primera vez).
    /// </summary>
    Task<IReadOnlyList<Guid>> ListAllTenantIdsAsync(CancellationToken ct = default);
}
