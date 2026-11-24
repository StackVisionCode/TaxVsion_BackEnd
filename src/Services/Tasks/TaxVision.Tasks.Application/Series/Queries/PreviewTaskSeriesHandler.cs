using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Series.Queries;

/// <param name="StartsOn">Por defecto, hoy en la oficina.</param>
public sealed record PreviewTaskSeriesQuery(
    Guid TenantId,
    string? Rule,
    RecurrenceMode Mode,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    int? MaxOccurrences,
    int Count
);

/// <summary>
/// Lo que produciría una serie: la primera fecha (la que se envía como ancla), las siguientes, el resumen y los avisos.
/// Una regla no válida no es un error de la petición: vuelve sin fechas y con el motivo entre los avisos.
/// </summary>
public sealed record TaskSeriesPreviewResponse(
    DateOnly? FirstDueOn,
    IReadOnlyList<DateOnly> NextDates,
    string Summary,
    IReadOnlyList<string> Warnings,
    string TimeZoneId,
    bool IsValid
);

public static class PreviewTaskSeriesHandler
{
    public static async Task<TaskSeriesPreviewResponse> Handle(
        PreviewTaskSeriesQuery query,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        var zone = await zones.ResolveAsync(query.TenantId, ct);
        var rule = RecurrenceRule.Create(query.Rule, zone.Id);
        if (rule.IsFailure || !Enum.IsDefined(query.Mode))
        {
            var reason = rule.IsFailure ? rule.Error.Message : "Choose when the next task is created.";
            return new TaskSeriesPreviewResponse(null, [], RecurrenceSummary.Custom, [reason], zone.Id, false);
        }

        var startsOn = query.StartsOn ?? CivilDate.Of(DateTime.UtcNow, zone);
        var count = query.Count <= 0 ? 5 : query.Count;
        var preview = RecurrencePreview.Compute(
            rule.Value,
            query.Mode,
            startsOn,
            query.EndsOn,
            query.MaxOccurrences,
            count
        );
        return new TaskSeriesPreviewResponse(
            preview.Dates.Count > 0 ? preview.Dates[0] : null,
            preview.Dates,
            RecurrenceSummary.Describe(rule.Value.Value, query.Mode),
            preview.Warnings,
            zone.Id,
            preview.Dates.Count > 0
        );
    }
}
