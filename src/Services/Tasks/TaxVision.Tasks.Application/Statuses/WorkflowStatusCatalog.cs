using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Statuses;

/// <summary>
/// Un estado del catálogo tal como lo ve la oficina. El id interno no sale: el identificador público es
/// el código. <c>RowVersion</c> en base64 para la concurrencia optimista; null en los del sistema sin fila.
/// </summary>
public sealed record WorkflowStatusResponse(
    string Code,
    string DisplayName,
    string Color,
    TaskItemStatus Category,
    bool IsDefault,
    bool IsSystem,
    bool IsArchived,
    int SortOrder,
    string? Description,
    string? RowVersion,
    int OpenTaskCount = 0
)
{
    public static WorkflowStatusResponse From(WorkflowStatus status) =>
        new(
            status.Code.Value,
            status.DisplayName,
            status.Color.Value,
            status.Category,
            status.IsDefault,
            status.IsSystem,
            status.IsArchived,
            status.SortOrder,
            status.Description,
            status.RowVersion.Length == 0 ? null : Convert.ToBase64String(status.RowVersion)
        );
}

public sealed record WorkflowStatusesResponse(IReadOnlyList<WorkflowStatusResponse> Statuses);

/// <summary>
/// Los cinco estados del sistema y la resolución de un código. Un estado del sistema sin fila existe igual:
/// es el que la oficina ve si no personalizó nada.
/// </summary>
public static class WorkflowStatusCatalog
{
    public static readonly TaskItemStatus[] Categories =
    [
        TaskItemStatus.NotStarted,
        TaskItemStatus.InProgress,
        TaskItemStatus.WaitingOnClient,
        TaskItemStatus.Completed,
        TaskItemStatus.Cancelled,
    ];

    public static string SystemName(TaskItemStatus category) =>
        category switch
        {
            TaskItemStatus.NotStarted => "Not started",
            TaskItemStatus.InProgress => "In progress",
            TaskItemStatus.WaitingOnClient => "Waiting on client",
            TaskItemStatus.Completed => "Completed",
            _ => "Cancelled",
        };

    public static LabelColor SystemColor(TaskItemStatus category) =>
        LabelColor
            .Create(
                category switch
                {
                    TaskItemStatus.NotStarted => "#64748B",
                    TaskItemStatus.InProgress => "#2563EB",
                    TaskItemStatus.WaitingOnClient => "#D97706",
                    TaskItemStatus.Completed => "#16A34A",
                    _ => "#94A3B8",
                }
            )
            .Value;

    /// <summary>Las filas de la oficina más los del sistema que no tienen fila, por etapa y orden.</summary>
    /// <summary>Tareas abiertas por estado; las que no tienen estado propio cuentan para el por defecto de su etapa.</summary>
    public static IReadOnlyList<WorkflowStatusResponse> WithOpenCounts(
        IReadOnlyList<WorkflowStatusResponse> statuses,
        IReadOnlyList<OpenTaskTally> tallies
    ) =>
        [
            .. statuses.Select(s =>
                s with
                {
                    OpenTaskCount = tallies
                        .Where(t => t.Category == s.Category && (t.Code == s.Code || (t.Code is null && s.IsDefault)))
                        .Sum(t => t.Count),
                }
            ),
        ];

    public static IReadOnlyList<WorkflowStatusResponse> Compose(IReadOnlyList<WorkflowStatus> rows)
    {
        var all = rows.Select(WorkflowStatusResponse.From).ToList();
        foreach (var category in Categories)
        {
            var systemCode = TaskLabelCode.SystemCodeOf(category);
            if (rows.Any(r => r.Code == systemCode))
                continue;

            var customDefault = rows.Any(r => r.Category == category && r.IsDefault && !r.IsArchived);
            all.Add(
                new(
                    systemCode.Value,
                    SystemName(category),
                    SystemColor(category).Value,
                    category,
                    !customDefault,
                    true,
                    false,
                    0,
                    null,
                    null
                )
            );
        }

        return [.. all.OrderBy(s => s.Category).ThenBy(s => s.SortOrder).ThenBy(s => s.DisplayName)];
    }

    /// <summary>
    /// El código que llega por la API → referencia validada para la tarea. Un estado de otra oficina no se
    /// encuentra. El del sistema sin fila es el por defecto (null) mientras la oficina no haya elegido otro; si
    /// lo eligió, se crea su fila para poder señalarlo.
    /// </summary>
    public static async Task<Result<WorkflowStatusRef>> ResolveAsync(
        IWorkflowStatusRepository statuses,
        Guid tenantId,
        string? code,
        Guid byUserId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var parsed = TaskLabelCode.Create(code);
        if (parsed.IsFailure)
            return Result.Failure<WorkflowStatusRef>(TaskErrors.Status.NotFound);

        var row = await statuses.GetByCodeAsync(tenantId, parsed.Value, ct);
        if (row is not null)
            return row.IsArchived
                ? Result.Failure<WorkflowStatusRef>(TaskErrors.Status.Archived)
                : Result.Success(row.ToRef());

        var category = Categories.FirstOrDefault(c => TaskLabelCode.SystemCodeOf(c) == parsed.Value);
        if (!TaskLabelCode.IsReserved(parsed.Value))
            return Result.Failure<WorkflowStatusRef>(TaskErrors.Status.NotFound);

        return Result.Success(await SystemRefAsync(statuses, tenantId, category, byUserId, nowUtc, ct));
    }

    private static async Task<WorkflowStatusRef> SystemRefAsync(
        IWorkflowStatusRepository statuses,
        Guid tenantId,
        TaskItemStatus category,
        Guid byUserId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var rows = await statuses.ListAsync(tenantId, false, ct);
        if (!rows.Any(r => r.Category == category && r.IsDefault))
            return WorkflowStatusRef.DefaultOf(category);

        var materialized = WorkflowStatus
            .CustomizeSystem(tenantId, category, SystemName(category), SystemColor(category), false, byUserId, nowUtc)
            .Value;
        statuses.Add(materialized);
        return materialized.ToRef();
    }
}
