using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Labels.Abstractions;
using TaxVision.Tasks.Domain.Labels;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

public sealed class TaskLabelRepository(TasksDbContext context) : ITaskLabelRepository
{
    public void Add(TaskLabel label) => context.TaskLabels.Add(label);

    public void Remove(TaskLabel label) => context.TaskLabels.Remove(label);

    public async Task<Result<TaskLabel>> GetByIdAsync(Guid tenantId, Guid labelId, CancellationToken ct = default)
    {
        var label = await context
            .TaskLabels.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Id == labelId, ct);

        return label is null ? Result.Failure<TaskLabel>(TaskErrors.Label.NotFound) : Result.Success(label);
    }

    /// <summary>
    /// El índice único ya lo impide, pero sin esta consulta el choque sale como 409 genérico de
    /// persistencia en vez de decir cuál es el código repetido.
    /// </summary>
    public Task<bool> CodeExistsAsync(
        Guid tenantId,
        TaskLabelCode code,
        Guid? excludingLabelId,
        CancellationToken ct = default
    ) =>
        context
            .TaskLabels.IgnoreQueryFilters()
            .AnyAsync(l => l.TenantId == tenantId && l.Code == code && l.Id != excludingLabelId, ct);

    public async Task<IReadOnlyList<TaskLabel>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        await context
            .TaskLabels.IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.DisplayName)
            .ToListAsync(ct);
}
