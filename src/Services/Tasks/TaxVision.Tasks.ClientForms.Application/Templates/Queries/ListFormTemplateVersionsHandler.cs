using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Queries;

public sealed record ListFormTemplateVersionsQuery(Guid TenantId, Guid TemplateId);

public static class ListFormTemplateVersionsHandler
{
    // Una plantilla inexistente o de otro tenant es 404, no una lista vacía.
    public static async Task<Result<IReadOnlyList<FormTemplateVersionSummaryResponse>>> Handle(
        ListFormTemplateVersionsQuery query,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        CancellationToken ct
    )
    {
        var template = await templates.GetByIdAsync(query.TenantId, query.TemplateId, ct);
        if (template.IsFailure)
            return Result.Failure<IReadOnlyList<FormTemplateVersionSummaryResponse>>(template.Error);
        var found = await versions.ListAsync(query.TenantId, query.TemplateId, ct);
        return Result.Success<IReadOnlyList<FormTemplateVersionSummaryResponse>>([
            .. found.Select(FormTemplateVersionSummaryResponse.From),
        ]);
    }
}
