using TaxVision.Correspondence.Domain.Backfill;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>Marca de "backfill de CustomerEmailAddresses ya corrido" por tenant (Fase 2).</summary>
public interface ITenantBackfillStateRepository
{
    Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(TenantBackfillState entity, CancellationToken ct = default);

    /// <summary>
    /// Fase 16 — universo de tenants que <c>MailboxOwnershipReconciliationJob</c> (COR-10) debe
    /// recorrer: los tenants que Correspondence ya descubrió. Un tenant sin fila sigue preguntando a Connectors.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListAllTenantIdsAsync(CancellationToken ct = default);
}
