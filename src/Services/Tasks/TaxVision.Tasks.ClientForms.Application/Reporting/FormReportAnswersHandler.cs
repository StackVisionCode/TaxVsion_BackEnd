using BuildingBlocks.Common;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

public static class FormReportAnswersHandler
{
    public const int PageSize = 50;

    public static async Task<PagedResult<FormReportAnswerRow>> Handle(
        FormReportAnswersQuery query,
        IFormAnswerIndexReadRepository read,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        CancellationToken ct
    )
    {
        var normalized = query with
        {
            Page = Math.Max(1, query.Page),
            Key = query.Key.Trim(),
            Value = string.IsNullOrWhiteSpace(query.Value) ? null : query.Value.Trim(),
            Contains = string.IsNullOrWhiteSpace(query.Contains) ? null : query.Contains.Trim(),
        };
        var page = await read.SearchAsync(normalized, ct);
        var labels =
            (await FormReportTemplateSchema.LatestAsync(query.TenantId, query.TemplateId, versions, engine, ct))?.Labels
            ?? FormReportLabels.Empty;
        return page with
        {
            Items = page
                .Items.Select(row => row with { ValueLabel = labels.Of(row.VersionNumber, row.Key, row.ValueText) })
                .ToArray(),
        };
    }
}
