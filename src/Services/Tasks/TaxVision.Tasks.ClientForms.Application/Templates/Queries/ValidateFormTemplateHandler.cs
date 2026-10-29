using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Queries;

// Con Capabilities también reporta la cobertura del mapeo, igual que al publicar.
public sealed record ValidateFormTemplateQuery(
    Guid TenantId,
    Guid TemplateId,
    PublisherCapabilities? Capabilities = null
);

public static class ValidateFormTemplateHandler
{
    public static async Task<Result<FormTemplateValidationResponse>> Handle(
        ValidateFormTemplateQuery query,
        IFormEngine engine,
        IFormTemplateRepository templates,
        IDataCollectionTargetCatalog targetCatalog,
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
        var report = engine.ValidateForPublish(parsed.Value);
        if (!report.IsValid || query.Capabilities is null)
            return Result.Success(new FormTemplateValidationResponse(report.Issues));
        var mappings = engine.ValidateMappings(parsed.Value, targetCatalog.GetSnapshot(), query.Capabilities);
        return Result.Success(new FormTemplateValidationResponse(mappings.Issues));
    }
}
