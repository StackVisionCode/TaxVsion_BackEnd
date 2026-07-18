using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.ValueObjects;

public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Result<Money> Create(decimal amount, string currency)
    {
        if (amount < 0)
            return Result.Failure<Money>(new Error("Money.NegativeAmount", "Amount cannot be negative."));

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            return Result.Failure<Money>(new Error("Money.InvalidCurrency", "Currency must be a 3-letter ISO code."));

        return Result.Success(new Money(amount, currency.Trim().ToUpperInvariant()));
    }

    public static Money Zero(string currency) => new(0m, currency.Trim().ToUpperInvariant());
}
