using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

/// <summary>
/// Tablero por columnas: una consulta agrupada para los totales y otra por columna con solo las columnas de la
/// tarjeta. No materializa agregados.
/// </summary>
public sealed class TaskBoardReader(TasksDbContext context) : ITaskBoardReader
{
    public async Task<IReadOnlyList<TaskBoardTally>> CountAsync(
        Guid tenantId,
        TaskQueryFilter filter,
        Guid? assignedToUserId,
        CancellationToken ct = default
    ) =>
        await TaskQueryFiltering
            .Filtered(context, tenantId, filter, assignedToUserId)
            .GroupBy(t => new { t.Status, t.WorkflowStatusCode })
            .Select(g => new TaskBoardTally(g.Key.Status, g.Key.WorkflowStatusCode, g.Count()))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TaskCardResponse>> ListCardsAsync(
        Guid tenantId,
        TaskQueryFilter filter,
        TaskBoardSort sort,
        int skip,
        int take,
        Guid? assignedToUserId,
        CancellationToken ct = default
    )
    {
        var rows = await Ordered(TaskQueryFiltering.Filtered(context, tenantId, filter, assignedToUserId), sort)
            .Skip(skip)
            .Take(take)
            .Select(t => new CardRow(
                t.Id,
                t.Title.Value,
                t.Status,
                t.WorkflowStatusCode,
                t.Priority,
                t.AssigneeUserId,
                t.Reference.CustomerId,
                t.Reference.TaxYear,
                t.Due == null ? null : t.Due.DueAtUtc,
                t.Due == null ? null : t.Due.TimeZoneId,
                t.Due != null && t.Due.IsStatutory,
                t.OpenBlockerCount,
                t.OpenSubtaskCount,
                t.SubtaskCount,
                t.Estimated,
                t.ExpectedItems,
                t.CompletedAtUtc,
                t.SeriesId != null
            ))
            .ToListAsync(ct);
        return [.. rows.Select(ToCard)];
    }

    private static IOrderedQueryable<TaskItem> Ordered(IQueryable<TaskItem> query, TaskBoardSort sort) =>
        sort switch
        {
            TaskBoardSort.RecentlyCompleted => query.OrderByDescending(t => t.CompletedAtUtc).ThenBy(t => t.Id),
            TaskBoardSort.Priority => query
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.Due == null)
                .ThenBy(t => t.Due!.DueAtUtc)
                .ThenBy(t => t.Id),
            _ => query.OrderBy(t => t.Due == null).ThenBy(t => t.Due!.DueAtUtc).ThenBy(t => t.Id),
        };

    // Las columnas convertidas (estimado, petición al cliente) salen enteras y se abren aquí; la fecha civil se calcula en memoria con la misma regla que DueDate.DueOn.
    private static TaskCardResponse ToCard(CardRow row) =>
        new(
            row.Id,
            row.Title,
            row.Status,
            row.WorkflowStatusCode,
            row.Priority,
            row.AssigneeUserId,
            row.CustomerId,
            row.TaxYear,
            row.DueAtUtc,
            row.DueAtUtc is { } due ? CivilDueOn(due, row.DueTimeZoneId) : null,
            row.DueTimeZoneId,
            row.DueIsStatutory,
            row.OpenBlockerCount > 0,
            row.OpenBlockerCount,
            row.OpenSubtaskCount,
            row.SubtaskCount,
            row.Estimated?.Value,
            row.ExpectedItems?.Value,
            row.CompletedAtUtc,
            row.IsRecurring
        );

    private static DateOnly CivilDueOn(DateTime dueAtUtc, string? timeZoneId) =>
        dueAtUtc.TimeOfDay == TimeSpan.Zero
            ? DateOnly.FromDateTime(dueAtUtc)
            : CivilDate.Of(DateTime.SpecifyKind(dueAtUtc, DateTimeKind.Utc), ResolvedTimeZone.Resolve(timeZoneId));

    private sealed record CardRow(
        Guid Id,
        string Title,
        TaskItemStatus Status,
        string? WorkflowStatusCode,
        TaskPriority Priority,
        Guid? AssigneeUserId,
        Guid? CustomerId,
        int? TaxYear,
        DateTime? DueAtUtc,
        string? DueTimeZoneId,
        bool DueIsStatutory,
        int OpenBlockerCount,
        int OpenSubtaskCount,
        int SubtaskCount,
        EstimatedHours? Estimated,
        ClientRequestNote? ExpectedItems,
        DateTime? CompletedAtUtc,
        bool IsRecurring
    );
}
