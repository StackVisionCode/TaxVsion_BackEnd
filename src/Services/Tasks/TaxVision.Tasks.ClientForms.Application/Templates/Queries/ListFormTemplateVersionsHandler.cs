using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Queries;

public sealed record ListFormTemplateVersionsQuery(Guid TenantId, Guid TemplateId);

public static class ListFormTemplateVersionsHandler
{
    public static async Task<IReadOnlyList<FormTemplateVersionSummaryResponse>> Handle(
        ListFormTemplateVersionsQuery query,
        IFormTemplateVersionRepository versions,
        CancellationToken ct
    ) =>
        [
            .. (await versions.ListAsync(query.TenantId, query.TemplateId, ct)).Select(
                FormTemplateVersionSummaryResponse.From
            ),
        ];
}
