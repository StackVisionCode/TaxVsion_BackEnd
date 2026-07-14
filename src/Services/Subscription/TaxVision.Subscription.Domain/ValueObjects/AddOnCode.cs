using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.ValueObjects;

public sealed partial record AddOnCode
{
    public string Value { get; }

    private AddOnCode(string value) => Value = value;

    public static Result<AddOnCode> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !ValidPattern().IsMatch(value))
        {
            return Result.Failure<AddOnCode>(
                new Error("AddOnCode.Invalid", "Add-on code must match ^[a-z][a-z0-9._-]{2,49}$."));
        }

        return Result.Success(new AddOnCode(value));
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z][a-z0-9._-]{2,49}$")]
    private static partial Regex ValidPattern();
}
