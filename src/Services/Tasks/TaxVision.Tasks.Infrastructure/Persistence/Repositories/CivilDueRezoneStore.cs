using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks.TimeZones;
using TaxVision.Tasks.ClientForms.Application.Requests.TimeZones;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

/// <summary>P9: lo abierto con vencimiento de fecha civil, cargado para recalcular; el dominio decide qué cambia.</summary>
internal sealed class CivilDueRezoneStore(TasksDbContext context) : ICivilDueRezoneStore, IFormRequestRezoneStore
{
    public async Task<IReadOnlyList<TaskItem>> OpenTasksWithCivilDuesAsync(
        Guid tenantId,
        IReadOnlyCollection<string> timeZoneIds,
        CancellationToken ct
    ) =>
        await context
            .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(t =>
                t.TenantId == tenantId
                && t.Status != TaskItemStatus.Completed
                && t.Status != TaskItemStatus.Cancelled
                && ((t.Due != null && timeZoneIds.Contains(t.Due.TimeZoneId)) || t.ClientDueOn != null)
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ClientRequest>> OpenClientRequestsWithDueAsync(
        Guid tenantId,
        CancellationToken ct
    ) =>
        await context
            .ClientRequests.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(r =>
                r.TenantId == tenantId
                && r.DueOn != null
                && (r.Status == ClientRequestStatus.Pending || r.Status == ClientRequestStatus.Submitted)
            )
            .ToListAsync(ct);

    // Pausadas también: al reanudar tienen que vencer en la zona nueva.
    public async Task<IReadOnlyList<TaskSeries>> SeriesToRezoneAsync(
        Guid tenantId,
        IReadOnlyCollection<string> timeZoneIds,
        CancellationToken ct
    ) =>
        (
            await context
                .TaskSeries.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
                .Where(s => s.TenantId == tenantId && s.Status != SeriesStatus.Ended)
                .ToListAsync(ct)
        )
            // La zona vive dentro de la regla (objeto de valor): el filtro fino se hace en memoria.
            .Where(s => timeZoneIds.Contains(s.Rule.TimeZoneId))
            .ToList();

    public async Task<IReadOnlyList<FormRequest>> OpenWithDueAsync(Guid tenantId, CancellationToken ct) =>
        await context
            .FormRequests.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(r =>
                r.TenantId == tenantId
                && r.DueOn != null
                && r.Status != FormRequestStatus.Completed
                && r.Status != FormRequestStatus.Cancelled
            )
            .ToListAsync(ct);
}
