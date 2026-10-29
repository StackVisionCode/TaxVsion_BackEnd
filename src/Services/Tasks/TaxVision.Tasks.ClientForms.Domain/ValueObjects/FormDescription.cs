using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Domain.ValueObjects;

public sealed record FormDescription
{
    public const int MaxLength = 1000;

    private FormDescription(string value) => Value = value;

    public string Value { get; }

    public static Result<FormDescription?> Create(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return Result.Success<FormDescription?>(null);
        return normalized.Length > MaxLength
            ? Result.Failure<FormDescription?>(FormTemplateErrors.InvalidDescription)
            : Result.Success<FormDescription?>(new FormDescription(normalized));
    }
}
