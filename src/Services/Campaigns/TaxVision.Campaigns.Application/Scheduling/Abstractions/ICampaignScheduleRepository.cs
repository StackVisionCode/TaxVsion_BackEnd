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

    /// <summary>
    /// Agendados vivos (Active/Paused) que disparan en nombre de <paramref name="userId"/>: los que heredó
    /// (<c>RunAsUserId</c>) o los que agendó y nadie heredó. Rastreados, para traspasarlos o pausarlos.
    /// </summary>
    Task<IReadOnlyList<CampaignSchedule>> ListOpenRunAsUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );

    /// <summary>Cuántos agendados vivos disparan en nombre de <paramref name="userId"/> (pre-flight del retiro).</summary>
    Task<int> CountOpenRunAsUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Agendados que ya quedaron pausados por el retiro de <paramref name="fromUserId"/> (para no repetir).</summary>
    Task<IReadOnlySet<Guid>> ListPausedOnOffboardingAsync(
        Guid tenantId,
        Guid fromUserId,
        CancellationToken ct = default
    );

    /// <summary>Constancia del traspaso o la pausa por retiro. Solo se agrega.</summary>
    Task AddHandoverAsync(CampaignScheduleHandover handover, CancellationToken ct = default);
}
