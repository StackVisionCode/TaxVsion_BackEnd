using BuildingBlocks.Common;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Queries;

public sealed record ListFormTemplatesQuery(
    Guid TenantId,
    FormTemplateStatus? Status,
    FormPurpose? Purpose,
    string? Search,
    int Page,
    int PageSize
);

public static class ListFormTemplatesHandler
{
    public static async Task<PagedResult<FormTemplateSummaryResponse>> Handle(
        ListFormTemplatesQuery query,
        IFormTemplateRepository templates,
        CancellationToken ct
    )
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var found = await templates.ListAsync(query.TenantId, query.Status, query.Purpose, query.Search, page, size, ct);
        return new PagedResult<FormTemplateSummaryResponse>(
            found.Items.Select(FormTemplateSummaryResponse.From).ToArray(),
            found.Page,
            found.Size,
            found.TotalCount
        );
    }
}
