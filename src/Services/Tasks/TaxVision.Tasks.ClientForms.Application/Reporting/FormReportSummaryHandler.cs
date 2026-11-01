using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

public static class FormReportSummaryHandler
{
    public static async Task<FormReportSummaryResponse> Handle(
        FormReportSummaryQuery query,
        IFormAnswerIndexReadRepository read,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        CancellationToken ct
    )
    {
        var normalized = query with { Key = query.Key.Trim() };
        var labels =
            (await FormReportTemplateSchema.LatestAsync(query.TenantId, query.TemplateId, versions, engine, ct))?.Labels
            ?? FormReportLabels.Empty;
        var buckets = (await read.SummarizeAsync(normalized, ct))
            .Select(bucket => bucket with { Label = labels.Latest(normalized.Key, bucket.Value) })
            .ToArray();
        return new FormReportSummaryResponse(
            normalized.TemplateId,
            normalized.Key,
            buckets.Sum(bucket => bucket.Count),
            buckets
        );
    }
}
