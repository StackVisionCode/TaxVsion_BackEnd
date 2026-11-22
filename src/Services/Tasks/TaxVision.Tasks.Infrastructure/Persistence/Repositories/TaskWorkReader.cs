using System.Linq.Expressions;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Work;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

/// <summary>
/// Conteos agrupados en SQL (pocas consultas fijas, sin traer filas) y un top acotado. Pedido al cliente: responsable
/// = <c>FollowUpUserId ?? RequestedByUserId</c>, el mismo destinatario de sus avisos y del retiro.
/// </summary>
public sealed class TaskWorkReader(TasksDbContext context) : ITaskWorkReader
{
    private static readonly TaskItemStatus[] OpenStatuses =
    [
        TaskItemStatus.NotStarted,
        TaskItemStatus.InProgress,
        TaskItemStatus.WaitingOnClient,
    ];

    private sealed record CountRow(Guid UserId, int Open, int Overdue, int DueSoon, int Waiting);

    public async Task<IReadOnlyList<WorkCount>> CountAsync(
        Guid tenantId,
        Guid userId,
        Guid? visibleTo,
        WorkWindow window,
        CancellationToken ct
    )
    {
        Guid[] users = [userId];
        var tasks = (await TaskCountsAsync(tenantId, users, visibleTo, window, full: true, ct)).FirstOrDefault();
        var requests = await RequestCounts(tenantId, users, visibleTo, window).FirstOrDefaultAsync(ct);
        return [ToCount(WorkKinds.Tasks, tasks), ToCount(WorkKinds.ClientRequests, requests)];
    }

    public async Task<IReadOnlyList<WorkBatchRow>> CountByUserAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        Guid? visibleTo,
        WorkWindow window,
        CancellationToken ct
    )
    {
        var tasks = await TaskCountsAsync(tenantId, userIds, visibleTo, window, full: false, ct);
        var requests = await RequestCounts(tenantId, userIds, visibleTo, window).ToListAsync(ct);
        return tasks
            .Concat(requests)
            .GroupBy(row => row.UserId)
            .Select(g => new WorkBatchRow(g.Key, g.Sum(r => r.Open), g.Sum(r => r.Overdue)))
            .ToList();
    }

    public async Task<IReadOnlyList<OfficeWaitingCount>> CountOfficeWaitingAsync(
        Guid tenantId,
        Guid? visibleTo,
        CancellationToken ct
    )
    {
        var tasks = await OpenTasks(tenantId, visibleTo)
            .CountAsync(t => t.Status == TaskItemStatus.WaitingOnClient, ct);
        var requests = await OpenRequests(tenantId, visibleTo)
            .CountAsync(r => r.Status == ClientRequestStatus.Pending, ct);
        return
        [
            new OfficeWaitingCount(WorkKinds.Tasks, tasks),
            new OfficeWaitingCount(WorkKinds.ClientRequests, requests),
        ];
    }

    public async Task<IReadOnlyList<WorkItem>> TopAsync(
        Guid tenantId,
        Guid userId,
        Guid? visibleTo,
        int take,
        CancellationToken ct
    )
    {
        var tasks = await AssignedTasks(tenantId, userId, visibleTo).Take(take).AsNoTracking().ToListAsync(ct);
        var requests = await AssignedRequests(tenantId, userId, visibleTo).Take(take).AsNoTracking().ToListAsync(ct);
        return [.. tasks.Select(ToItem), .. requests.Select(ToItem)];
    }

    public Task<PagedResult<WorkItem>> ItemsAsync(
        Guid tenantId,
        Guid userId,
        Guid? visibleTo,
        string kind,
        int page,
        int size,
        CancellationToken ct
    ) =>
        kind == WorkKinds.ClientRequests
            ? PageAsync(AssignedRequests(tenantId, userId, visibleTo), ToItem, page, size, ct)
            : PageAsync(AssignedTasks(tenantId, userId, visibleTo), ToItem, page, size, ct);

    public async Task<IReadOnlyList<WorkTitle>> TitlesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        IReadOnlyCollection<Guid> clientRequestIds,
        Guid? visibleTo,
        CancellationToken ct
    )
    {
        // Un WHERE Id IN (...) por tipo, con la regla de lectura del detalle.
        var tasks =
            taskIds.Count == 0
                ? []
                : await context
                    .Tasks.IgnoreQueryFilters()
                    .Where(t => t.TenantId == tenantId && taskIds.Contains(t.Id))
                    .VisibleTo(context, tenantId, visibleTo)
                    .Select(t => new WorkTitle(WorkResourceTypes.Task, t.Id, t.Title.Value))
                    .ToListAsync(ct);
        var requests =
            clientRequestIds.Count == 0
                ? []
                : await VisibleRequests(
                        context
                            .ClientRequests.IgnoreQueryFilters()
                            .Where(r => r.TenantId == tenantId && clientRequestIds.Contains(r.Id)),
                        tenantId,
                        visibleTo
                    )
                    .Select(r => new WorkTitle(WorkResourceTypes.ClientRequest, r.Id, r.Title))
                    .ToListAsync(ct);
        return [.. tasks, .. requests];
    }

    // Universo y orden del top y de la lista: el vencimiento más viejo primero, sin vencimiento al final.
    private IQueryable<TaskItem> AssignedTasks(Guid tenantId, Guid userId, Guid? visibleTo) =>
        OpenTasks(tenantId, visibleTo)
            .Where(t => t.AssigneeUserId == userId)
            .OrderBy(t => t.Due == null)
            .ThenBy(t => t.Due!.DueAtUtc)
            .ThenBy(t => t.Id);

    private IQueryable<ClientRequest> AssignedRequests(Guid tenantId, Guid userId, Guid? visibleTo) =>
        OpenRequests(tenantId, visibleTo)
            .Where(r => (r.FollowUpUserId ?? r.RequestedByUserId) == userId)
            .OrderBy(r => r.DueAtUtc == null)
            .ThenBy(r => r.DueAtUtc)
            .ThenBy(r => r.Id);

    private static async Task<PagedResult<WorkItem>> PageAsync<T>(
        IQueryable<T> ordered,
        Func<T, WorkItem> toItem,
        int page,
        int size,
        CancellationToken ct
    )
        where T : class
    {
        var total = await ordered.CountAsync(ct);
        var rows = await ordered.Skip((page - 1) * size).Take(size).AsNoTracking().ToListAsync(ct);
        return new PagedResult<WorkItem>([.. rows.Select(toItem)], page, size, total);
    }

    // Una consulta agrupada por métrica: el vencimiento es un owned type y EF no lo proyecta dentro de un GroupBy.
    // Sin <paramref name="full"/> (el lote) solo abiertas y vencidas.
    private async Task<IReadOnlyList<CountRow>> TaskCountsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        Guid? visibleTo,
        WorkWindow window,
        bool full,
        CancellationToken ct
    )
    {
        var mine = OpenTasks(tenantId, visibleTo)
            .Where(t => t.AssigneeUserId != null && userIds.Contains(t.AssigneeUserId.Value));
        var open = await CountByAssigneeAsync(mine, ct);
        var overdue = await CountByAssigneeAsync(mine.Where(Overdue(window)), ct);
        var dueSoon = full ? await CountByAssigneeAsync(mine.Where(DueSoon(window)), ct) : [];
        var waiting = full
            ? await CountByAssigneeAsync(mine.Where(t => t.Status == TaskItemStatus.WaitingOnClient), ct)
            : [];
        return open.Select(pair => new CountRow(
                pair.Key,
                pair.Value,
                overdue.GetValueOrDefault(pair.Key),
                dueSoon.GetValueOrDefault(pair.Key),
                waiting.GetValueOrDefault(pair.Key)
            ))
            .ToList();
    }

    private static Task<Dictionary<Guid, int>> CountByAssigneeAsync(IQueryable<TaskItem> tasks, CancellationToken ct) =>
        tasks
            .GroupBy(t => t.AssigneeUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

    // Regla de WorkDue. Un vencimiento a medianoche UTC es una fecha civil del dato viejo (P5): se compara por día.
    private static Expression<Func<TaskItem, bool>> Overdue(WorkWindow window)
    {
        var (now, today) = (window.NowUtc, window.TodayMidnightUtc);
        return t =>
            (t.Due!.DueAtUtc.TimeOfDay == TimeSpan.Zero && t.Due.DueAtUtc < today)
            || (t.Due!.DueAtUtc.TimeOfDay != TimeSpan.Zero && t.Due.DueAtUtc < now);
    }

    private static Expression<Func<TaskItem, bool>> DueSoon(WorkWindow window)
    {
        var (now, soonEnd) = (window.NowUtc, window.DueSoonEndUtc);
        var (today, soonDay) = (window.TodayMidnightUtc, window.DueSoonEndMidnightUtc);
        return t =>
            (t.Due!.DueAtUtc.TimeOfDay == TimeSpan.Zero && t.Due.DueAtUtc >= today && t.Due.DueAtUtc < soonDay)
            || (t.Due!.DueAtUtc.TimeOfDay != TimeSpan.Zero && t.Due.DueAtUtc >= now && t.Due.DueAtUtc < soonEnd);
    }

    // Fecha civil guardada; sin ella, el día UTC del instante (ClientRequest.DueCivilDate).
    private IQueryable<CountRow> RequestCounts(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        Guid? visibleTo,
        WorkWindow window
    )
    {
        var (today, soonEnd) = (window.Today, window.DueSoonEnd);
        var (todayUtc, soonEndUtc) = (window.TodayMidnightUtc, window.DueSoonEndMidnightUtc);
        return OpenRequests(tenantId, visibleTo)
            .Select(r => new
            {
                UserId = r.FollowUpUserId ?? r.RequestedByUserId,
                r.DueOn,
                r.DueAtUtc,
                r.Status,
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
                g.Count(x => x.Status == ClientRequestStatus.Pending)
            ));
    }

    private IQueryable<TaskItem> OpenTasks(Guid tenantId, Guid? visibleTo) =>
        context
            .Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && OpenStatuses.Contains(t.Status))
            .VisibleTo(context, tenantId, visibleTo);

    private IQueryable<ClientRequest> OpenRequests(Guid tenantId, Guid? visibleTo) =>
        VisibleRequests(
            context
                .ClientRequests.IgnoreQueryFilters()
                .Where(r =>
                    r.TenantId == tenantId
                    && (r.Status == ClientRequestStatus.Pending || r.Status == ClientRequestStatus.Submitted)
                ),
            tenantId,
            visibleTo
        );

    // Mismo criterio que el listado de tareas: lo propio (lo pidió o lo sigue) o un cliente asignado a quien mira.
    private IQueryable<ClientRequest> VisibleRequests(
        IQueryable<ClientRequest> requests,
        Guid tenantId,
        Guid? visibleTo
    )
    {
        if (visibleTo is not { } me)
            return requests;

        var myClients = context
            .CustomerAssignmentProjections.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.UserId == me);
        return requests.Where(r =>
            r.RequestedByUserId == me || r.FollowUpUserId == me || myClients.Any(a => a.CustomerId == r.CustomerId)
        );
    }

    private static WorkCount ToCount(string kind, CountRow? row) =>
        new(kind, row?.Open ?? 0, row?.Overdue ?? 0, row?.DueSoon ?? 0, row?.Waiting ?? 0);

    // Fin de día civil (o el dato viejo a medianoche UTC) viaja como fecha; con hora, como instante.
    private static WorkItem ToItem(TaskItem task)
    {
        var due = task.Due;
        var civil = due is not null && (due.IsEndOfCivilDay || due.DueAtUtc.TimeOfDay == TimeSpan.Zero);
        return new WorkItem(
            task.Id,
            WorkKinds.Tasks,
            task.Title.Value,
            task.Reference.CustomerId,
            civil ? null : due?.DueAtUtc,
            civil ? due!.DueOn : null,
            task.Status.ToString(),
            WorkLinks.Task(task.Id)
        );
    }

    private static WorkItem ToItem(ClientRequest request) =>
        new(
            request.Id,
            WorkKinds.ClientRequests,
            request.Title,
            request.CustomerId,
            null,
            request.DueCivilDate,
            request.Status.ToString(),
            WorkLinks.ClientRequest(request.CustomerId, request.Id)
        );
}
