using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

/// <summary>Los filtros de búsqueda, tablero y calendario, una sola vez, con la visibilidad de lectura al final.</summary>
internal static class TaskQueryFiltering
{
    private static readonly TaskItemStatus[] OpenStatuses = TaskItemStatuses.Open;

    internal static IQueryable<TaskItem> Filtered(
        TasksDbContext context,
        Guid tenantId,
        TaskQueryFilter filter,
        Guid? assignedToUserId
    )
    {
        var query = context.Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly).Where(t => t.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var text = filter.Text.Trim();
            query = query.Where(t => t.Title.Value.Contains(text));
        }

        query = WithStatusCode(query, filter.StatusCode);

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

        if (filter.Priority is { } priority)
            query = query.Where(t => t.Priority == priority);

        if (filter.RootOnly && filter.AlsoSubtasksOf is { } mine)
            query = query.Where(t => t.ParentTaskId == null || t.AssigneeUserId == mine);
        else if (filter.RootOnly)
            query = query.Where(t => t.ParentTaskId == null);

        // El corte del resumen del dashboard, no uno copiado: la lista y el número no pueden diferir.
        if (filter.Due is { } due)
            query = TaskSummaryReader.WithDue(query, due);

        return query.VisibleTo(context, tenantId, assignedToUserId);
    }

    private static IQueryable<TaskItem> WithStatusCode(IQueryable<TaskItem> query, TaskStatusCodeFilter? statusCode)
    {
        if (statusCode is { IsCategoryDefault: true, ExcludedCodes: { } excluded } catchAll)
            return query.Where(t =>
                t.Status == catchAll.Category
                && (t.WorkflowStatusCode == null || !excluded.Contains(t.WorkflowStatusCode))
            );
        if (statusCode is { IsCategoryDefault: true } byDefault)
            return query.Where(t =>
                t.Status == byDefault.Category
                && (t.WorkflowStatusCode == byDefault.Code || t.WorkflowStatusCode == null)
            );
        return statusCode is { } byCode ? query.Where(t => t.WorkflowStatusCode == byCode.Code) : query;
    }
}
