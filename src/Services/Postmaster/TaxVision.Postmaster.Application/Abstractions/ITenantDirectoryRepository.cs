using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Application.Abstractions;

public interface ITenantDirectoryRepository
{
    Task<TenantDirectoryEntry?> FindAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Crea o actualiza la fila de la oficina. Es upsert y no Add porque lo llaman dos caminos que
    /// pueden cruzarse: el consumer del evento (tenant nuevo) y el backfill de arranque (tenants que
    /// ya existían). Con Add, el primer arranque tras desplegar esto reventaría por clave duplicada
    /// en cuanto un tenant recién creado llegara por los dos lados.
    /// </summary>
    Task UpsertAsync(Guid tenantId, string name, string subDomain, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Los ids que YA están proyectados, para que el backfill no reescriba lo que no hace falta.</summary>
    Task<IReadOnlySet<Guid>> GetKnownTenantIdsAsync(CancellationToken ct = default);
}
