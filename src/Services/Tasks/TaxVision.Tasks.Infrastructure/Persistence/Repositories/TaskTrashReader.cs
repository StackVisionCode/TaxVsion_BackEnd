using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

public sealed class TaskTrashReader(TasksDbContext context) : ITaskTrashReader
{
    // La papelera ignora todos los filtros y fija la oficina a mano (TenantId en la expresión).
    private IQueryable<TaskItem> TrashRoots(Guid tenantId) =>
        context
            .Tasks.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.DeletedAtUtc != null && t.TrashRootId == t.Id);

    public async Task<TaskTrashPage> ListAsync(
        Guid tenantId,
        Guid? visibleTo,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var roots = TrashRoots(tenantId);
        if (visibleTo is { } viewer)
            roots = roots.Where(t =>
                t.AssigneeUserId == viewer || t.CreatedByUserId == viewer || t.DeletedByUserId == viewer
            );

        var total = await roots.CountAsync(ct);
        var rows = await roots
            .OrderByDescending(t => t.DeletedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Reference.CustomerId,
                DeletedAtUtc = t.DeletedAtUtc!.Value,
                t.DeletedByUserId,
                Branch = context.Tasks.IgnoreQueryFilters().Count(c => c.TenantId == tenantId && c.TrashRootId == t.Id),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new TaskTrashItem(
                r.Id,
                r.Title.Value,
                r.CustomerId,
                r.DeletedAtUtc,
                r.DeletedByUserId,
                Math.Max(0, r.Branch - 1),
                r.DeletedAtUtc.AddDays(TaskItem.TrashRetentionDays)
            ))
            .ToList();
        return new TaskTrashPage(items, total);
    }

    // Cross-tenant: la purga recorre todas las oficinas.
    public async Task<IReadOnlyList<(Guid TenantId, Guid RootId)>> ListExpiredRootsAsync(
        DateTime deletedBeforeUtc,
        int take,
        CancellationToken ct = default
    ) =>
        (
            await context
                .Tasks.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.DeletedAtUtc != null && t.DeletedAtUtc < deletedBeforeUtc && t.TrashRootId == t.Id)
                .OrderBy(t => t.DeletedAtUtc)
                .Take(take)
                .Select(t => new { t.TenantId, t.Id })
                .ToListAsync(ct)
        )
            .Select(t => (t.TenantId, t.Id))
            .ToList();
}
