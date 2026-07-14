using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.ValueObjects;

public sealed record GracePeriod
{
    public int Days { get; }

    private GracePeriod(int days) => Days = days;

    public static Result<GracePeriod> Create(int days)
    {
        if (days is < 0 or > 90)
            return Result.Failure<GracePeriod>(new Error("GracePeriod.Invalid", "Grace period must be between 0 and 90 days."));

        return Result.Success(new GracePeriod(days));
    }
}
