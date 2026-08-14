using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>
/// El alta de una raíz y la de una subtarea publican el mismo evento; armarlo dos veces es donde los
/// dos payloads empiezan a divergir.
/// </summary>
public static class TaskCreatedEventFactory
{
    public static TaskCreatedIntegrationEvent From(TaskItem task, string correlationId) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            TaskId = task.Id,
            Title = task.Title.Value,
            Priority = task.Priority.ToString(),
            AssigneeUserId = task.AssigneeUserId,
            CustomerId = task.Reference.CustomerId,
            TaxYear = task.Reference.TaxYear,
            DueAtUtc = task.Due?.DueAtUtc,
            ParentTaskId = task.ParentTaskId,
        };
}
