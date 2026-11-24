using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Labels;

public sealed record TaskLabelResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string Color,
    TaskItemStatus MapsToStatus,
    int SortOrder
)
{
    public static TaskLabelResponse From(WorkflowStatus label) =>
        new(label.Id, label.Code.Value, label.DisplayName, label.Color.Value, label.Category, label.SortOrder);
}
