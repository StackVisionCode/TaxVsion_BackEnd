using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Queries;

public sealed record GetFormTemplateQuery(Guid TenantId, Guid TemplateId);

public static class GetFormTemplateHandler
{
    public static async Task<Result<FormTemplateDetailResponse>> Handle(
        GetFormTemplateQuery query,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(query.TenantId, query.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure<FormTemplateDetailResponse>(found.Error);
        var published = await versions.ListAsync(query.TenantId, query.TemplateId, ct);
        return Result.Success(
            FormTemplateDetailResponse.From(
                found.Value,
                published.Select(FormTemplateVersionSummaryResponse.From).ToArray()
            )
        );
    }
}
