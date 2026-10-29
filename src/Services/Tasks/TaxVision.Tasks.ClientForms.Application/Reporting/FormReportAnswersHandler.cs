using BuildingBlocks.Common;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

public static class FormReportAnswersHandler
{
    public const int PageSize = 50;

    public static Task<PagedResult<FormReportAnswerRow>> Handle(
        FormReportAnswersQuery query,
        IFormAnswerIndexReadRepository read,
        CancellationToken ct
    )
    {
        var normalized = query with
        {
            Page = Math.Max(1, query.Page),
            Key = query.Key.Trim(),
            Value = string.IsNullOrWhiteSpace(query.Value) ? null : query.Value.Trim(),
        };
        return read.SearchAsync(normalized, ct);
    }
}
