using BuildingBlocks.Common;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Abstractions;

/// <summary>Historial de actividad. Todas las consultas llevan el tenant explícito (corren sin tenant ambiental).</summary>
public interface IActivityEntryRepository
{
    Task<bool> ExistsAsync(Guid tenantId, Guid eventId, CancellationToken ct = default);

    void Add(ActivityEntry entry);

    /// <summary>Página más reciente primero, ya filtrada por lo que el que mira puede ver.</summary>
    Task<PagedResult<ActivityEntry>> ListAsync(ActivityFeedFilter filter, CancellationToken ct = default);
}
