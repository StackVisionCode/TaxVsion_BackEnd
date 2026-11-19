using BuildingBlocks.Contracts.Work;

namespace TaxVision.Tasks.Application.Work;

/// <summary>Une las partes del proveedor (tareas y pedidos al cliente, form requests) en la respuesta común.</summary>
public static class WorkSummaryComposer
{
    public const string Provider = "tasks";
    public const string Module = "Tasks and client forms";

    /// <param name="forms">null cuando Client Forms no aplica (sin permiso o sin módulo): no se inventa un cero.</param>
    public static WorkSummaryResponse Compose(TaskWork tasks, WorkCount? forms, IReadOnlyList<WorkItem> formsTop)
    {
        IReadOnlyList<WorkCount> counts = forms is null ? tasks.Counts : [.. tasks.Counts, forms];
        return new WorkSummaryResponse(Provider, Module, counts, MostUrgent(tasks.Top.Concat(formsTop)));
    }

    /// <summary>Una fila por persona pedida, con las partes sumadas.</summary>
    public static WorkBatchResponse Combine(IReadOnlyList<Guid> userIds, params IReadOnlyList<WorkBatchRow>[] parts)
    {
        var rows = parts.SelectMany(part => part).ToLookup(row => row.UserId);
        return new WorkBatchResponse(
            userIds
                .Distinct()
                .Select(id => new WorkBatchRow(id, rows[id].Sum(r => r.Open), rows[id].Sum(r => r.Overdue)))
                .ToList()
        );
    }

    // El vencimiento más viejo arriba (las vencidas quedan primero); a igual día, el de hora antes que el de fin de
    // día; sin vencimiento al final.
    private static IReadOnlyList<WorkItem> MostUrgent(IEnumerable<WorkItem> items) =>
        items
            .OrderBy(item => DueDay(item) is null)
            .ThenBy(DueDay)
            .ThenBy(item => item.DueAtUtc is null)
            .ThenBy(item => item.DueAtUtc)
            .Take(WorkLimits.TopItems)
            .ToList();

    private static DateOnly? DueDay(WorkItem item) =>
        item.DueDate ?? (item.DueAtUtc is { } at ? DateOnly.FromDateTime(at) : null);
}
