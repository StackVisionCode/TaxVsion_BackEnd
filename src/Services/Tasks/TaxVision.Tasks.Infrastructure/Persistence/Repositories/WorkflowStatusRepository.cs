using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

public sealed class WorkflowStatusRepository(TasksDbContext context) : IWorkflowStatusRepository
{
    private static readonly TaskItemStatus[] OpenStatuses = TaskItemStatuses.Open;

    public void Add(WorkflowStatus status) => context.WorkflowStatuses.Add(status);

    public void Remove(WorkflowStatus status) => context.WorkflowStatuses.Remove(status);

    private IQueryable<WorkflowStatus> OfTenant(Guid tenantId) =>
        context.WorkflowStatuses.IgnoreQueryFilters(TaskQueryFilters.TenantOnly).Where(s => s.TenantId == tenantId);

    public async Task<Result<WorkflowStatus>> GetByIdAsync(Guid tenantId, Guid statusId, CancellationToken ct = default)
    {
        var status = await OfTenant(tenantId).FirstOrDefaultAsync(s => s.Id == statusId, ct);
        return status is null ? Result.Failure<WorkflowStatus>(TaskErrors.Label.NotFound) : Result.Success(status);
    }

    public Task<WorkflowStatus?> GetByCodeAsync(Guid tenantId, TaskLabelCode code, CancellationToken ct = default) =>
        OfTenant(tenantId).FirstOrDefaultAsync(s => s.Code == code, ct);

    /// <summary>
    /// El índice único ya lo impide, pero sin esta consulta el choque sale como 409 genérico de
    /// persistencia en vez de decir cuál es el código repetido.
    /// </summary>
    public Task<bool> CodeExistsAsync(
        Guid tenantId,
        TaskLabelCode code,
        Guid? excludingStatusId,
        CancellationToken ct = default
    ) => OfTenant(tenantId).AnyAsync(s => s.Code == code && s.Id != excludingStatusId, ct);

    public async Task<IReadOnlyList<WorkflowStatus>> ListAsync(
        Guid tenantId,
        bool includeArchived = false,
        CancellationToken ct = default
    ) =>
        await OfTenant(tenantId)
            .Where(s => includeArchived || !s.IsArchived)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.DisplayName)
            .ToListAsync(ct);

    public async Task<IReadOnlySet<string>> ListCodesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var codes = await OfTenant(tenantId).Select(s => s.Code).ToListAsync(ct);
        return codes.Select(c => c.Value).ToHashSet();
    }

    // La intercalación de la columna no distingue mayúsculas: la comparación en SQL ya lo hace.
    public Task<bool> NameTakenAsync(
        Guid tenantId,
        string displayName,
        Guid? excludingStatusId,
        CancellationToken ct = default
    ) =>
        OfTenant(tenantId)
            .AnyAsync(s => !s.IsArchived && s.DisplayName == displayName.Trim() && s.Id != excludingStatusId, ct);

    public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
        OfTenant(tenantId).CountAsync(s => !s.IsArchived, ct);

    public Task<int> CountOpenTasksAsync(Guid tenantId, Guid statusId, CancellationToken ct = default) =>
        context
            .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .CountAsync(
                t => t.TenantId == tenantId && t.WorkflowStatusId == statusId && OpenStatuses.Contains(t.Status),
                ct
            );

    public async Task<IReadOnlyList<OpenTaskTally>> CountOpenTasksByCodeAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await context
            .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(t => t.TenantId == tenantId && OpenStatuses.Contains(t.Status))
            .GroupBy(t => new { t.Status, t.WorkflowStatusCode })
            .Select(g => new OpenTaskTally(g.Key.Status, g.Key.WorkflowStatusCode, g.Count()))
            .ToListAsync(ct);

    public Task<bool> IsUsedAsync(Guid tenantId, Guid statusId, CancellationToken ct = default) =>
        context
            .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .AnyAsync(t => t.TenantId == tenantId && t.WorkflowStatusId == statusId, ct);

    public Task<int> MoveOpenTasksAsync(
        Guid tenantId,
        Guid fromStatusId,
        WorkflowStatusRef target,
        CancellationToken ct = default
    ) =>
        context
            .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(t =>
                t.TenantId == tenantId
                && t.WorkflowStatusId == fromStatusId
                && t.Status == target.Category
                && OpenStatuses.Contains(t.Status)
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(t => t.WorkflowStatusId, target.Id)
                        .SetProperty(t => t.WorkflowStatusCode, target.Id == null ? null : target.Code),
                ct
            );

    public void ExpectVersion(WorkflowStatus status, byte[] rowVersion) =>
        context.Entry(status).Property(s => s.RowVersion).OriginalValue = rowVersion;

    // Cross-tenant a propósito: lo corre el job de reconciliación, sin actor ni oficina.
    public async Task<int> ReleaseOpenTasksFromArchivedAsync(int take, CancellationToken ct = default)
    {
        var archived = context
            .WorkflowStatuses.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(s => s.IsArchived)
            .Select(s => s.Id);
        var ids = await context
            .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(t =>
                t.WorkflowStatusId != null
                && archived.Contains(t.WorkflowStatusId.Value)
                && OpenStatuses.Contains(t.Status)
            )
            .Select(t => t.Id)
            .Take(take)
            .ToListAsync(ct);
        if (ids.Count == 0)
            return 0;

        // Cross-tenant: los ids ya salieron de la consulta de arriba.
        return await context
            .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(t => ids.Contains(t.Id))
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(t => t.WorkflowStatusId, (Guid?)null)
                        .SetProperty(t => t.WorkflowStatusCode, (string?)null),
                ct
            );
    }
}
