using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Requests.Work;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

// A cargo = responsable o, sin él, quien la pidió (el mismo criterio del retiro). Visibilidad = la de la bandeja.
internal sealed class FormRequestWorkReader(TasksDbContext context) : IFormRequestWorkReader
{
    private sealed record CountRow(Guid UserId, int Open, int Overdue, int DueSoon, int Waiting);

    public async Task<WorkCount> CountAsync(
        Guid tenantId,
        Guid userId,
        Guid actorUserId,
        bool canViewAll,
        DateOnly today,
        CancellationToken ct
    )
    {
        Guid[] users = [userId];
        var row = await Counts(tenantId, users, actorUserId, canViewAll, today).FirstOrDefaultAsync(ct);
        return new WorkCount(
            WorkKinds.FormRequests,
            row?.Open ?? 0,
            row?.Overdue ?? 0,
            row?.DueSoon ?? 0,
            row?.Waiting ?? 0
        );
    }

    public async Task<IReadOnlyList<WorkItem>> TopAsync(
        Guid tenantId,
        Guid userId,
        Guid actorUserId,
        bool canViewAll,
        int take,
        CancellationToken ct
    ) => await ToItemsAsync(Assigned(tenantId, userId, actorUserId, canViewAll).Take(take), ct);

    public async Task<PagedResult<WorkItem>> ItemsAsync(
        Guid tenantId,
        Guid userId,
        Guid actorUserId,
        bool canViewAll,
        int page,
        int size,
        CancellationToken ct
    )
    {
        var assigned = Assigned(tenantId, userId, actorUserId, canViewAll);
        var total = await assigned.CountAsync(ct);
        var items = await ToItemsAsync(assigned.Skip((page - 1) * size).Take(size), ct);
        return new PagedResult<WorkItem>(items, page, size, total);
    }

    // Un WHERE Id IN (...) con la visibilidad de la bandeja, en cualquier estado.
    public async Task<IReadOnlyList<WorkTitle>> TitlesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> requestIds,
        Guid actorUserId,
        bool canViewAll,
        CancellationToken ct
    ) =>
        await context
            .FormRequests.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && requestIds.Contains(item.Id))
            .VisibleInInbox(context, tenantId, actorUserId, canViewAll)
            .Select(item => new WorkTitle(WorkResourceTypes.FormRequest, item.Id, item.Title))
            .ToListAsync(ct);

    // Universo y orden del top y de la lista: el vencimiento más viejo primero, sin vencimiento al final.
    private IQueryable<FormRequest> Assigned(Guid tenantId, Guid userId, Guid actorUserId, bool canViewAll) =>
        Open(tenantId, actorUserId, canViewAll)
            .Where(item => (item.ResponsibleUserId ?? item.RequestedByUserId) == userId)
            .OrderBy(item => item.DueAtUtc == null)
            .ThenBy(item => item.DueAtUtc)
            .ThenBy(item => item.Id);

    private static async Task<IReadOnlyList<WorkItem>> ToItemsAsync(IQueryable<FormRequest> rows, CancellationToken ct)
    {
        var found = await rows.Select(item => new
            {
                item.Id,
                item.Title,
                item.CustomerId,
                item.DueOn,
                item.DueAtUtc,
                item.Status,
            })
            .ToListAsync(ct);
        return found
            .Select(row => new WorkItem(
                row.Id,
                WorkKinds.FormRequests,
                row.Title,
                row.CustomerId,
                null,
                row.DueOn ?? (row.DueAtUtc is { } due ? DateOnly.FromDateTime(due) : null),
                row.Status.ToString(),
                FormRequestWork.LinkFor(row.Id)
            ))
            .ToList();
    }

    public async Task<IReadOnlyList<WorkBatchRow>> CountByUserAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        Guid actorUserId,
        bool canViewAll,
        DateOnly today,
        CancellationToken ct
    ) =>
        await Counts(tenantId, userIds, actorUserId, canViewAll, today)
            .Select(row => new WorkBatchRow(row.UserId, row.Open, row.Overdue))
            .ToListAsync(ct);

    // Fecha civil guardada; sin ella, el día UTC del instante (FormRequest.DueCivilDate). Esperando al cliente =
    // pendiente o devuelta para corregir.
    private IQueryable<CountRow> Counts(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        Guid actorUserId,
        bool canViewAll,
        DateOnly today
    )
    {
        var soonEnd = WorkDue.DueSoonEnd(today);
        var todayUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var soonEndUtc = soonEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return Open(tenantId, actorUserId, canViewAll)
            .Select(item => new
            {
                UserId = item.ResponsibleUserId ?? item.RequestedByUserId,
                item.DueOn,
                item.DueAtUtc,
                item.Status,
            })
            .Where(x => userIds.Contains(x.UserId))
            .GroupBy(x => x.UserId)
            .Select(g => new CountRow(
                g.Key,
                g.Count(),
                g.Count(x => (x.DueOn != null && x.DueOn < today) || (x.DueOn == null && x.DueAtUtc < todayUtc)),
                g.Count(x =>
                    (x.DueOn != null && x.DueOn >= today && x.DueOn < soonEnd)
                    || (x.DueOn == null && x.DueAtUtc >= todayUtc && x.DueAtUtc < soonEndUtc)
                ),
                g.Count(x => x.Status == FormRequestStatus.Pending || x.Status == FormRequestStatus.Returned)
            ));
    }

    private IQueryable<FormRequest> Open(Guid tenantId, Guid actorUserId, bool canViewAll) =>
        context
            .FormRequests.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.Status != FormRequestStatus.Completed
                && item.Status != FormRequestStatus.Cancelled
            )
            .VisibleInInbox(context, tenantId, actorUserId, canViewAll);
}
