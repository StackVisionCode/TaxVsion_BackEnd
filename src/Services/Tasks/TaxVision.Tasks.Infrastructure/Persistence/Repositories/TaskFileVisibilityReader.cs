using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Contracts.Work;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Files;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

/// <summary>La regla del drawer de la tarea, por archivo: adjunto disponible en una tarea que pasa <c>VisibleTo</c>.</summary>
public sealed class TaskFileVisibilityReader(TasksDbContext context) : ITaskFileVisibilityReader
{
    public async Task<IReadOnlyList<VisibleFile>> VisibleTaskFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        Guid? visibleTo,
        CancellationToken ct
    )
    {
        if (fileIds.Count == 0)
            return [];

        // Cuenta cualquier origen (subido, enlazado o de plantilla): es lo que el drawer muestra.
        var visibleTasks = context
            .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(t => t.TenantId == tenantId)
            .VisibleTo(context, tenantId, visibleTo);
        var rows = await context
            .Set<TaskAttachment>()
            .IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(a => a.TenantId == tenantId && fileIds.Contains(a.FileId) && a.Status == AttachmentStatus.Available)
            .Join(
                visibleTasks,
                a => a.TaskId,
                t => t.Id,
                (a, t) =>
                    new
                    {
                        a.FileId,
                        a.AttachedAtUtc,
                        TaskId = t.Id,
                        Title = t.Title.Value,
                        t.Reference.CustomerId,
                    }
            )
            .ToListAsync(ct);

        // Un mismo archivo en varias tareas visibles: abre la del adjunto más reciente.
        return
        [
            .. rows.GroupBy(r => r.FileId)
                .Select(g => g.OrderByDescending(r => r.AttachedAtUtc).ThenBy(r => r.TaskId).First())
                .Select(r => new VisibleFile(r.FileId, WorkResourceTypes.Task, r.TaskId, r.Title, r.CustomerId)),
        ];
    }
}
