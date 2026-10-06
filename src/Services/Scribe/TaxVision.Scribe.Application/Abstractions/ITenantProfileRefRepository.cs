using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Abstractions;

public interface ITenantProfileRefRepository
{
    Task<TenantProfileRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Idempotente: el evento se reentrega y el backfill puede pisar una fila ya proyectada.</summary>
    Task UpsertAsync(Guid tenantId, string name, string subDomain, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Ids ya proyectados. El backfill lo usa para saber qué le falta sin traerse las filas.</summary>
    Task<IReadOnlyCollection<Guid>> GetKnownTenantIdsAsync(CancellationToken ct = default);
}
