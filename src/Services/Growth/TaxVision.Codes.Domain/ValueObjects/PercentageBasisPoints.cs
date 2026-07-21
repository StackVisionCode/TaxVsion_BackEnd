using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.ValueObjects;

public sealed record PercentageBasisPoints
{
    public const int Maximum = 10_000;

    public int Value { get; }

    private PercentageBasisPoints(int value) => Value = value;

    public static Result<PercentageBasisPoints> Create(int value)
    {
        if (value is < 1 or > Maximum)
            return Result.Failure<PercentageBasisPoints>(
                new Error("Codes.PercentageBasisPoints.OutOfRange", $"Basis points must be between 1 and {Maximum}.")
            );

        return Result.Success(new PercentageBasisPoints(value));
    }

    public Result<Money> ApplyTo(Money grossAmount)
    {
        try
        {
            var amountCents = decimal.ToInt64(
                decimal.Round((decimal)grossAmount.AmountCents * Value / Maximum, 0, MidpointRounding.AwayFromZero)
            );

            return Money.Create(amountCents, grossAmount.Currency);
        }
        catch (OverflowException)
        {
            return Result.Failure<Money>(
                new Error("Codes.PercentageBasisPoints.Overflow", "The percentage calculation overflowed.")
            );
        }
    }
}
