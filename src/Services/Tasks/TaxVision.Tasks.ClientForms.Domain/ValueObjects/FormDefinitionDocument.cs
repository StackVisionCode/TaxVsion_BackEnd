using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Domain.ValueObjects;

public sealed record FormDefinitionDocument(string Json)
{
    public static Result<FormDefinitionDocument> Create(string? json, int maxBytes)
    {
        if (string.IsNullOrWhiteSpace(json) || System.Text.Encoding.UTF8.GetByteCount(json) > maxBytes)
            return Result.Failure<FormDefinitionDocument>(FormTemplateErrors.InvalidDefinition);
        return Result.Success(new FormDefinitionDocument(json));
    }
}
