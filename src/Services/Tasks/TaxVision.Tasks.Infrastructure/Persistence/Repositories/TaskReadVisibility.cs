using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;

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
            .IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(a => a.TenantId == tenantId && a.UserId == me);
        var tasks = context.Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly).Where(t => t.TenantId == tenantId);

        // Madre y abuela por LEFT JOIN a su clave y "cliente mío" por LEFT JOIN a la proyección (única por cliente y
        // usuario): ninguna fila se duplica y SQL Server lo resuelve en una pasada, sin EXISTS anidados por fila.
        return from t in query
            join parentRow in tasks on t.ParentTaskId equals (Guid?)parentRow.Id into parents
            from p in parents.DefaultIfEmpty()
            join grandparentRow in tasks on p.ParentTaskId equals (Guid?)grandparentRow.Id into grandparents
            from g in grandparents.DefaultIfEmpty()
            join ownClient in myClients on t.Reference.CustomerId equals (Guid?)ownClient.CustomerId into ownClients
            from mt in ownClients.DefaultIfEmpty()
            join parentClient in myClients
                on p.Reference.CustomerId equals (Guid?)parentClient.CustomerId
                into parentClients
            from mp in parentClients.DefaultIfEmpty()
            join grandparentClient in myClients
                on g.Reference.CustomerId equals (Guid?)grandparentClient.CustomerId
                into grandparentClients
            from mg in grandparentClients.DefaultIfEmpty()
            where
                t.AssigneeUserId == me
                || t.CreatedByUserId == me
                || mt != null
                || (t.ParentTaskId == null && t.Reference.CustomerId == null && t.AssigneeUserId == null)
                || (
                    p != null
                    && (
                        p.AssigneeUserId == me
                        || p.CreatedByUserId == me
                        || mp != null
                        || (p.ParentTaskId == null && p.Reference.CustomerId == null && p.AssigneeUserId == null)
                        || (
                            g != null
                            && (
                                g.AssigneeUserId == me
                                || g.CreatedByUserId == me
                                || mg != null
                                || (g.Reference.CustomerId == null && g.AssigneeUserId == null)
                            )
                        )
                    )
                )
            select t;
    }
}
