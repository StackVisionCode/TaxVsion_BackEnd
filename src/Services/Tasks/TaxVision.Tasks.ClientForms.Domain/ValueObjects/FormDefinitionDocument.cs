using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Domain.ValueObjects;

public sealed record FormDefinitionDocument
{
    // Techo absoluto; el límite efectivo lo fija la configuración del tenant y nunca lo supera.
    public const int MaxBytes = 5_242_880;

    private FormDefinitionDocument(string json) => Json = json;

    public string Json { get; }

    public static Result<FormDefinitionDocument> Create(string? json, int maxBytes)
    {
        if (
            string.IsNullOrWhiteSpace(json)
            || System.Text.Encoding.UTF8.GetByteCount(json) > Math.Min(maxBytes, MaxBytes)
        )
            return Result.Failure<FormDefinitionDocument>(FormTemplateErrors.InvalidDefinition);
        return Result.Success(new FormDefinitionDocument(json));
    }
}
