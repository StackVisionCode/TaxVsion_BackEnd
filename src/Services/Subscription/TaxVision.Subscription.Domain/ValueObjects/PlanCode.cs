using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.ValueObjects;

public sealed partial record PlanCode
{
    public string Value { get; }

    private PlanCode(string value) => Value = value;

    public static Result<PlanCode> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !ValidPattern().IsMatch(value))
        {
            return Result.Failure<PlanCode>(
                new Error("PlanCode.Invalid", "Plan code must match ^[a-z][a-z0-9_-]{2,49}$.")
            );
        }

        return Result.Success(new PlanCode(value));
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z][a-z0-9_-]{2,49}$")]
    private static partial Regex ValidPattern();
}
