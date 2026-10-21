using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Queries;

public sealed record GetFormTemplateVersionQuery(Guid TenantId, Guid TemplateId, int VersionNumber);

public static class GetFormTemplateVersionHandler
{
    public static async Task<Result<FormTemplateVersionResponse>> Handle(
        GetFormTemplateVersionQuery query,
        IFormTemplateVersionRepository versions,
        CancellationToken ct
    )
    {
        var found = await versions.GetAsync(query.TenantId, query.TemplateId, query.VersionNumber, ct);
        return found.IsFailure
            ? Result.Failure<FormTemplateVersionResponse>(found.Error)
            : Result.Success(FormTemplateVersionResponse.From(found.Value));
    }
}
