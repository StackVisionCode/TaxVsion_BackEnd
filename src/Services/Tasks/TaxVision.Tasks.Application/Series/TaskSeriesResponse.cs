using TaxVision.Tasks.Domain.Series;

namespace TaxVision.Tasks.Application.Series;

public sealed record TaskSeriesResponse(
    Guid Id,
    string Rule,
    string TimeZoneId,
    RecurrenceMode Mode,
    SeriesStatus Status,
    string Title,
    Guid AssigneeUserId,
    DateTime AnchorUtc,
    Guid? OpenInstanceId,
    int GeneratedCount,
    int SkippedOccurrences,
    DateTime? EndsAtUtc,
    int? MaxOccurrences
)
{
    public static TaskSeriesResponse From(TaskSeries series) =>
        new(
            series.Id,
            series.Rule.Value,
            series.Rule.TimeZoneId,
            series.Mode,
            series.Status,
            series.Blueprint.Title.Value,
            series.Blueprint.AssigneeUserId,
            series.AnchorUtc,
            series.OpenInstanceId,
            series.GeneratedCount,
            series.SkippedOccurrences,
            series.EndsAtUtc,
            series.MaxOccurrences
        );
}
