using BuildingBlocks.TimeZones;
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
    int? MaxOccurrences,
    string Summary = RecurrenceSummary.Custom,
    DateOnly? NextDueOn = null,
    bool IsStatutory = false,
    string? Description = null,
    Domain.Tasks.TaskPriority Priority = Domain.Tasks.TaskPriority.Normal,
    Guid? CustomerId = null,
    string? RowVersion = null
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
            series.MaxOccurrences,
            RecurrenceSummary.Describe(series.Rule.Value, series.Mode),
            NextDueOf(series),
            series.Blueprint.IsStatutory,
            series.Blueprint.Description?.Value,
            series.Blueprint.Priority,
            series.Blueprint.Reference.CustomerId,
            series.RowVersion.Length == 0 ? null : Convert.ToBase64String(series.RowVersion)
        );

    /// <summary>La fecha de la ocurrencia abierta o, sin ella, la siguiente que planificará; nada si terminó.</summary>
    private static DateOnly? NextDueOf(TaskSeries series)
    {
        if (series.Status == SeriesStatus.Ended)
            return null;
        var zone = ResolvedTimeZone.Resolve(series.Rule.TimeZoneId);
        var planned = series.LastPlannedDueUtc ?? series.AnchorUtc;
        if (series.OpenInstanceId is not null || series.GeneratedCount == 0)
            return CivilDate.Of(planned, zone);
        if (series.PendingFirstDueUtc is { } pending)
            return CivilDate.Of(pending, zone);
        var next = series.Rule.NextAfter(planned);
        return next.IsSuccess ? CivilDate.Of(next.Value, zone) : null;
    }
}
