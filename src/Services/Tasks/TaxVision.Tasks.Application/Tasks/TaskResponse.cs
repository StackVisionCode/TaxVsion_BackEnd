using BuildingBlocks.Common;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>
/// Aplana los VOs a propósito: el contrato HTTP no cambia porque un VO se reorganice por dentro.
/// <c>IsBlocked</c> viaja calculado para que el front no tenga que saber la regla del contador.
/// </summary>
public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    Guid CreatedByUserId,
    Guid? AssigneeUserId,
    Guid? CustomerId,
    int? TaxYear,
    DateTime? DueAtUtc,
    string? DueTimeZoneId,
    bool DueIsStatutory,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime CreatedAtUtc,
    Guid? ParentTaskId,
    int Depth,
    int OpenSubtaskCount,
    int OpenBlockerCount,
    bool IsBlocked,
    decimal? EstimatedHours,
    decimal ActualHours,
    string? ExpectedItems,
    DateTime? ClientDueAtUtc,
    Guid? ClientRequestedByUserId,
    DateTime? ClientRequestedAtUtc
)
{
    public static TaskResponse From(TaskItem task) =>
        new(
            task.Id,
            task.Title.Value,
            task.Description?.Value,
            task.Status,
            task.Priority,
            task.CreatedByUserId,
            task.AssigneeUserId,
            task.Reference.CustomerId,
            task.Reference.TaxYear,
            task.Due?.DueAtUtc,
            task.Due?.TimeZoneId,
            task.Due?.IsStatutory ?? false,
            task.StartedAtUtc,
            task.CompletedAtUtc,
            task.CreatedAtUtc,
            task.ParentTaskId,
            task.Depth,
            task.OpenSubtaskCount,
            task.OpenBlockerCount,
            task.IsBlocked,
            task.Estimated?.Value,
            task.ActualHours,
            task.ExpectedItems?.Value,
            task.ClientDueAtUtc,
            task.ClientRequestedByUserId,
            task.ClientRequestedAtUtc
        );

    public static PagedResult<TaskResponse> FromPage(PagedResult<TaskItem> page) =>
        new(page.Items.Select(From).ToList(), page.Page, page.Size, page.TotalCount);
}
