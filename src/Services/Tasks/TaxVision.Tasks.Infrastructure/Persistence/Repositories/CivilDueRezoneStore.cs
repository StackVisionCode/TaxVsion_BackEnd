using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks.TimeZones;
using TaxVision.Tasks.ClientForms.Application.Requests.TimeZones;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

/// <summary>P9: lo abierto con vencimiento de fecha civil, cargado para recalcular; el dominio decide qué cambia.</summary>
internal sealed class CivilDueRezoneStore(TasksDbContext context) : ICivilDueRezoneStore, IFormRequestRezoneStore
{
    public async Task<IReadOnlyList<TaskItem>> OpenTasksWithCivilDuesAsync(
        Guid tenantId,
        string previousTimeZoneId,
        CancellationToken ct
    ) =>
        await context
            .Tasks.IgnoreQueryFilters()
            .Where(t =>
                t.TenantId == tenantId
                && t.Status != TaskItemStatus.Completed
                && t.Status != TaskItemStatus.Cancelled
                && ((t.Due != null && t.Due.TimeZoneId == previousTimeZoneId) || t.ClientDueOn != null)
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ClientRequest>> OpenClientRequestsWithDueAsync(
        Guid tenantId,
        CancellationToken ct
    ) =>
        await context
            .ClientRequests.IgnoreQueryFilters()
            .Where(r =>
                r.TenantId == tenantId
                && r.DueOn != null
                && (r.Status == ClientRequestStatus.Pending || r.Status == ClientRequestStatus.Submitted)
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TaskSeries>> ActiveSeriesInZoneAsync(
        Guid tenantId,
        string previousTimeZoneId,
        CancellationToken ct
    ) =>
        (
            await context
                .TaskSeries.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId && s.Status == SeriesStatus.Active)
                .ToListAsync(ct)
        )
            // La zona vive dentro de la regla (objeto de valor): el filtro fino lo hace el dominio.
            .Where(s => s.Rule.TimeZoneId == previousTimeZoneId)
            .ToList();

    public async Task<IReadOnlyList<FormRequest>> OpenWithDueAsync(Guid tenantId, CancellationToken ct) =>
        await context
            .FormRequests.IgnoreQueryFilters()
            .Where(r =>
                r.TenantId == tenantId
                && r.DueOn != null
                && r.Status != FormRequestStatus.Completed
                && r.Status != FormRequestStatus.Cancelled
            )
            .ToListAsync(ct);
}
