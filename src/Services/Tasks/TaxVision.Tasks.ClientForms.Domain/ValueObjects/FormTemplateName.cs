using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Domain.ValueObjects;

public sealed record FormTemplateName(string Value)
{
    public const int MaxLength = 120;

    public static Result<FormTemplateName> Create(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxLength
            ? Result.Failure<FormTemplateName>(FormTemplateErrors.InvalidName)
            : Result.Success(new FormTemplateName(normalized));
    }
}
