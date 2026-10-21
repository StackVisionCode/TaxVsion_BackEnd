using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Formats;

namespace TaxVision.Tasks.ClientForms.Application.Common;

public static class StaffTextGuard
{
    public static readonly Error SensitiveData = new(
        "FormRequest.TextContainsSensitiveData",
        "Enter tax identifiers and payment card data only in their dedicated secure fields."
    );

    public static Result Validate(params string?[] values) =>
        values.Any(ContainsSensitiveData) ? Result.Failure(SensitiveData) : Result.Success();

    private static bool ContainsSensitiveData(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (TaxIdentifierFormat.LooksLikeTaxIdentifier(value) || SensitivePatternGuard.LooksLikeCardNumber(value));
}
