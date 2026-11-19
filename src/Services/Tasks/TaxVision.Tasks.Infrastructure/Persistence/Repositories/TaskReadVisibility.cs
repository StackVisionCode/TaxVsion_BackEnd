using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

/// <summary>Regla única de visibilidad de lectura de tareas, componible en SQL: listados, detalle y conteos de trabajo.</summary>
internal static class TaskReadVisibility
{
    // Visibilidad por asignación. Una tarea raíz se ve si el actor la tiene asignada, la creó, su cliente está
    // asignado a él o es de la bandeja común (sin cliente y sin asignar). Una subtarea sigue a su madre: se ve
    // si es trabajo propio o si se ve la madre. Se desenrolla hasta la abuela porque TaskItem.MaxDepth = 2.
    // IgnoreQueryFilters + tenant explícito (scope de Wolverine sin tenant ambiental). null = sin restricción.
    internal static IQueryable<TaskItem> VisibleTo(
        this IQueryable<TaskItem> query,
        TasksDbContext context,
        Guid tenantId,
        Guid? viewerUserId
    )
    {
        if (viewerUserId is not { } me)
            return query;

        var myClients = context
            .Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.UserId == me);
        var tasks = context.Tasks.IgnoreQueryFilters().Where(t => t.TenantId == tenantId);

        return query.Where(t =>
            t.AssigneeUserId == me
            || t.CreatedByUserId == me
            || myClients.Any(a => a.CustomerId == t.Reference.CustomerId)
            || (t.ParentTaskId == null && t.Reference.CustomerId == null && t.AssigneeUserId == null)
            || tasks.Any(p =>
                p.Id == t.ParentTaskId
                && (
                    p.AssigneeUserId == me
                    || p.CreatedByUserId == me
                    || myClients.Any(a => a.CustomerId == p.Reference.CustomerId)
                    || (p.ParentTaskId == null && p.Reference.CustomerId == null && p.AssigneeUserId == null)
                    || tasks.Any(g =>
                        g.Id == p.ParentTaskId
                        && (
                            g.AssigneeUserId == me
                            || g.CreatedByUserId == me
                            || myClients.Any(a => a.CustomerId == g.Reference.CustomerId)
                            || (g.Reference.CustomerId == null && g.AssigneeUserId == null)
                        )
                    )
                )
            )
        );
    }
}
