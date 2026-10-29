namespace TaxVision.Tasks.ClientForms.Application.Reporting;

public static class FormReportSummaryHandler
{
    public static async Task<FormReportSummaryResponse> Handle(
        FormReportSummaryQuery query,
        IFormAnswerIndexReadRepository read,
        CancellationToken ct
    )
    {
        var normalized = query with { Key = query.Key.Trim() };
        var buckets = await read.SummarizeAsync(normalized, ct);
        return new FormReportSummaryResponse(
            normalized.TemplateId,
            normalized.Key,
            buckets.Sum(bucket => bucket.Count),
            buckets
        );
    }
}
