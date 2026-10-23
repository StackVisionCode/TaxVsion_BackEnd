using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Domain.ValueObjects;

public sealed record FormTemplateName
{
    public const int MaxLength = 120;

    private FormTemplateName(string value) => Value = value;

    public string Value { get; }

    public static Result<FormTemplateName> Create(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxLength
            ? Result.Failure<FormTemplateName>(FormTemplateErrors.InvalidName)
            : Result.Success(new FormTemplateName(normalized));
    }
}
