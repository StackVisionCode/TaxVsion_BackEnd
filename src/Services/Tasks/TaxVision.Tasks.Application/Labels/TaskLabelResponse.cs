using TaxVision.Tasks.Domain.Labels;
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
    public static TaskLabelResponse From(TaskLabel label) =>
        new(label.Id, label.Code.Value, label.DisplayName, label.Color.Value, label.MapsToStatus, label.SortOrder);
}
