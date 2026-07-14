using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.ValueObjects;

public sealed partial record EntitlementKey
{
    public string Value { get; }

    private EntitlementKey(string value) => Value = value;

    public static Result<EntitlementKey> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !ValidPattern().IsMatch(value))
        {
            return Result.Failure<EntitlementKey>(
                new Error("EntitlementKey.Invalid", "Entitlement key must match ^[a-z][a-z0-9._]{2,99}$."));
        }

        return Result.Success(new EntitlementKey(value));
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z][a-z0-9._]{2,99}$")]
    private static partial Regex ValidPattern();
}
