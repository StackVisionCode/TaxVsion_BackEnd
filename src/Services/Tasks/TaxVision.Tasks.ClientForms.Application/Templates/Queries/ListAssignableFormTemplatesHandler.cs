using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Queries;

public sealed record ListAssignableFormTemplatesQuery(Guid TenantId, string? Search);

public static class ListAssignableFormTemplatesHandler
{
    public static async Task<IReadOnlyList<AssignableFormTemplateResponse>> Handle(
        ListAssignableFormTemplatesQuery query,
        IFormTemplateRepository templates,
        CancellationToken ct
    ) =>
        [
            .. (await templates.ListAssignableAsync(query.TenantId, query.Search, ct)).Select(item =>
                new AssignableFormTemplateResponse(
                    item.Template.Id,
                    item.Template.Name.Value,
                    item.Template.Purpose,
                    item.Template.LatestVersionNumber,
                    item.Title
                )
            ),
        ];
}
