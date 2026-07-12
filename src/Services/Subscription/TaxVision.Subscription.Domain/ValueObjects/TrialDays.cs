using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.ValueObjects;

public sealed record TrialDays
{
    public int Value { get; }

    private TrialDays(int value) => Value = value;

    public static Result<TrialDays> Create(int value)
    {
        if (value is < 0 or > 90)
            return Result.Failure<TrialDays>(new Error("TrialDays.Invalid", "Trial days must be between 0 and 90."));

        return Result.Success(new TrialDays(value));
    }
}
