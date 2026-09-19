using BuildingBlocks.Common;
using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Application.Scheduling.Abstractions;

public interface ICampaignScheduleRepository
{
    Task<CampaignSchedule?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<PagedResult<CampaignSchedule>> ListByCampaignAsync(
        Guid tenantId,
        Guid campaignId,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>Schedule dueño de un run activo (para liberar el guard de solape al completarse el run).</summary>
    Task<CampaignSchedule?> GetByActiveRunIdAsync(Guid tenantId, Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Reclama atómicamente (lease) hasta <paramref name="batchSize"/> schedules vencidos y devuelve las
    /// entidades reclamadas (rastreadas, para dispararlas y avanzarlas). Cross-tenant: lo llama el
    /// scheduler sin contexto de tenant.
    /// </summary>
    Task<IReadOnlyList<CampaignSchedule>> ClaimDueAsync(
        DateTime nowUtc,
        int leaseSeconds,
        int batchSize,
        CancellationToken ct = default
    );

    Task AddAsync(CampaignSchedule schedule, CancellationToken ct = default);
}
