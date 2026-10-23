using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Domain.ValueObjects;

public sealed partial record ContentHash
{
    private ContentHash(string value) => Value = value;

    public string Value { get; }

    public static Result<ContentHash> Create(string? value) =>
        value is not null && HashPattern().IsMatch(value)
            ? Result.Success(new ContentHash(value))
            : Result.Failure<ContentHash>(FormTemplateErrors.InvalidContentHash);

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
