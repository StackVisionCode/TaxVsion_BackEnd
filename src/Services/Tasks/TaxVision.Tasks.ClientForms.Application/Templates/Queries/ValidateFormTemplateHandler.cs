using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Queries;

public sealed record ValidateFormTemplateQuery(Guid TenantId, Guid TemplateId);

public static class ValidateFormTemplateHandler
{
    public static async Task<Result<FormTemplateValidationResponse>> Handle(
        ValidateFormTemplateQuery query,
        IFormEngine engine,
        IFormTemplateRepository templates,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(query.TenantId, query.TemplateId, ct);
        if (found.IsFailure)
            return Result.Failure<FormTemplateValidationResponse>(found.Error);
        var parsed = engine.ParseDefinition(found.Value.Draft.Json);
        if (parsed.IsFailure)
            return Result.Success(
                new FormTemplateValidationResponse([TemplateApplicationSupport.ParseIssue(parsed.Error)])
            );
        return Result.Success(new FormTemplateValidationResponse(engine.ValidateForPublish(parsed.Value).Issues));
    }
}
