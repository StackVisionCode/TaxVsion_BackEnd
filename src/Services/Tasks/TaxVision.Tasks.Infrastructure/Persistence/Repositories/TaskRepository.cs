using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

public sealed class TaskRepository(TasksDbContext context) : ITaskRepository
{
    /// <summary>Lo que no llegó a un terminal.</summary>
    private static readonly TaskItemStatus[] OpenStatuses =
    [
        TaskItemStatus.NotStarted,
        TaskItemStatus.InProgress,
        TaskItemStatus.WaitingOnClient,
    ];

    public void Add(TaskItem task) => context.Tasks.Add(task);

    public void Remove(TaskItem task) => context.Tasks.Remove(task);

    public async Task<IReadOnlyList<Guid>> ListChildIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> parentTaskIds,
        CancellationToken ct = default
    )
    {
        if (parentTaskIds.Count == 0)
            return [];

        return await context
            .Tasks.IgnoreQueryFilters()
            .Where(t =>
                t.TenantId == tenantId && t.ParentTaskId != null && parentTaskIds.Contains(t.ParentTaskId.Value)
            )
            .Select(t => t.Id)
            .ToListAsync(ct);
    }

    public async Task<Result<TaskItem>> GetByIdAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
    {
        var task = await context
            .Tasks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == taskId, ct);

        return task is null ? Result.Failure<TaskItem>(TaskErrors.NotFound) : Result.Success(task);
    }

    public async Task<Result<TaskItem>> GetByIdWithTimersAsync(
        Guid tenantId,
        Guid taskId,
        CancellationToken ct = default
    )
    {
        var task = await context
            .Tasks.IgnoreQueryFilters()
            .Include(t => t.Timers)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == taskId, ct);

        return task is null ? Result.Failure<TaskItem>(TaskErrors.NotFound) : Result.Success(task);
    }

    public async Task<Result<TaskItem>> GetByIdWithAttachmentsAsync(
        Guid tenantId,
        Guid taskId,
        CancellationToken ct = default
    )
    {
        var task = await context
            .Tasks.IgnoreQueryFilters()
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == taskId, ct);

        return task is null ? Result.Failure<TaskItem>(TaskErrors.NotFound) : Result.Success(task);
    }

    public async Task<TaskItem?> GetByAttachmentFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        await context
            .Tasks.IgnoreQueryFilters()
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Attachments.Any(a => a.FileId == fileId), ct);

    public async Task<IReadOnlyList<TaskItem>> ListWithAttachmentsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default
    ) =>
        await context
            .Tasks.IgnoreQueryFilters()
            .Include(t => t.Attachments)
            .Where(t => t.TenantId == tenantId && taskIds.Contains(t.Id))
            .ToListAsync(ct);

    public async Task<PagedResult<TaskItem>> ListSubtasksAsync(
        Guid tenantId,
        Guid parentTaskId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var ordered = context
            .Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.ParentTaskId == parentTaskId)
            .OrderBy(t => t.Due == null)
            .ThenBy(t => t.Due!.DueAtUtc)
            .ThenBy(t => t.Id);

        return await PageAsync(ordered, page, size, ct);
    }

    public async Task<PagedResult<TaskItem>> SearchAsync(
        Guid tenantId,
        TaskQueryFilter filter,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var ordered = Filtered(tenantId, filter).OrderByDescending(t => t.CreatedAtUtc).ThenBy(t => t.Id);
        return await PageAsync(ordered, page, size, ct);
    }

    /// <summary>
    /// Sin paginar: un tablero se pinta entero. El tope acota lo que un tenant grande puede pedir de
    /// una vez.
    /// </summary>
    public async Task<IReadOnlyList<TaskItem>> ListForBoardAsync(
        Guid tenantId,
        TaskQueryFilter filter,
        int take,
        CancellationToken ct = default
    ) =>
        await Filtered(tenantId, filter)
            .OrderBy(t => t.Due == null)
            .ThenBy(t => t.Due!.DueAtUtc)
            .ThenBy(t => t.Id)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TaskItem>> ListForCalendarAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? assigneeUserId,
        int take,
        CancellationToken ct = default
    )
    {
        var query = context
            .Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.Due!.DueAtUtc >= fromUtc && t.Due!.DueAtUtc <= toUtc);

        if (assigneeUserId is { } assignee)
            query = query.Where(t => t.AssigneeUserId == assignee);

        return await query.OrderBy(t => t.Due!.DueAtUtc).ThenBy(t => t.Id).Take(take).ToListAsync(ct);
    }

    private IQueryable<TaskItem> Filtered(Guid tenantId, TaskQueryFilter filter)
    {
        var query = context.Tasks.IgnoreQueryFilters().Where(t => t.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var text = filter.Text.Trim();
            query = query.Where(t => t.Title.Value.Contains(text));
        }

        if (filter.Status is { } status)
            query = query.Where(t => t.Status == status);
        else if (filter.OnlyOpen)
            query = query.Where(t => OpenStatuses.Contains(t.Status));

        if (filter.AssigneeUserId is { } assignee)
            query = query.Where(t => t.AssigneeUserId == assignee);

        if (filter.CustomerId is { } customer)
            query = query.Where(t => t.Reference.CustomerId == customer);

        if (filter.TaxYear is { } taxYear)
            query = query.Where(t => t.Reference.TaxYear == taxYear);

        return query;
    }

    public async Task<PagedResult<TaskItem>> ListForAssigneeAsync(
        Guid tenantId,
        Guid assigneeUserId,
        TaskItemStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = context
            .Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.AssigneeUserId == assigneeUserId);

        query = status is { } wanted
            ? query.Where(t => t.Status == wanted)
            : query.Where(t => OpenStatuses.Contains(t.Status));

        // Las sin vencimiento van al final. El primer criterio compara `Due`, la referencia owned:
        // `Due.DueAtUtc` es DateTime no-nullable y la comparación sería siempre falsa (CS8073).
        var ordered = query.OrderBy(t => t.Due == null).ThenBy(t => t.Due!.DueAtUtc).ThenBy(t => t.Id);

        return await PageAsync(ordered, page, size, ct);
    }

    public async Task<PagedResult<TaskItem>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int? taxYear,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = context
            .Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.Reference.CustomerId == customerId);

        if (taxYear is { } year)
            query = query.Where(t => t.Reference.TaxYear == year);

        var ordered = query.OrderByDescending(t => t.CreatedAtUtc).ThenBy(t => t.Id);
        return await PageAsync(ordered, page, size, ct);
    }

    public async Task<PagedResult<TaskItem>> ListWaitingOnClientAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var ordered = context
            .Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.Status == TaskItemStatus.WaitingOnClient)
            .OrderBy(t => t.ClientDueAtUtc == null)
            .ThenBy(t => t.ClientDueAtUtc)
            .ThenBy(t => t.Id);

        return await PageAsync(ordered, page, size, ct);
    }

    /// <summary>
    /// Cross-tenant. Sin <c>IgnoreQueryFilters()</c> el filtro fail-closed compara contra
    /// <c>Guid.Empty</c> y el job barre 0 filas para siempre, sin fallar ni loguear.
    /// </summary>
    public async Task<IReadOnlyList<TaskItem>> ListOverdueAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct = default
    ) =>
        await context
            .Tasks.IgnoreQueryFilters()
            .Where(t => OpenStatuses.Contains(t.Status) && t.Due!.DueAtUtc < nowUtc && t.OverdueNotifiedAtUtc == null)
            .OrderBy(t => t.Due!.DueAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetAncestorIdsAsync(
        Guid tenantId,
        Guid taskId,
        CancellationToken ct = default
    )
    {
        var ancestors = new List<Guid>();
        var currentId = taskId;

        // Cota por Depth máxima, no por el grafo: si algún día alguien mete un ciclo en la jerarquía
        // esto termina igual en vez de girar para siempre.
        for (var hop = 0; hop < TaskItem.MaxDepth; hop++)
        {
            var parentId = await context
                .Tasks.IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId && t.Id == currentId)
                .Select(t => t.ParentTaskId)
                .FirstOrDefaultAsync(ct);

            if (parentId is not { } parent)
                break;

            ancestors.Add(parent);
            currentId = parent;
        }

        return ancestors;
    }

    public async Task<IReadOnlyList<TaskItem>> ListByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default
    )
    {
        if (taskIds.Count == 0)
            return [];

        return await context
            .Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && taskIds.Contains(t.Id))
            .ToListAsync(ct);
    }

    private static async Task<PagedResult<TaskItem>> PageAsync(
        IQueryable<TaskItem> ordered,
        int page,
        int size,
        CancellationToken ct
    )
    {
        var totalCount = await ordered.CountAsync(ct);
        var items = await ordered.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<TaskItem>(items, page, size, totalCount);
    }
}
