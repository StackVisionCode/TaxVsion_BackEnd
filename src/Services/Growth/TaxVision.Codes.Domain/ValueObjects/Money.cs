using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.ValueObjects;

public sealed record Money
{
    public long AmountCents { get; }
    public string Currency { get; }

    private Money(long amountCents, string currency)
    {
        AmountCents = amountCents;
        Currency = currency;
    }

    public static Result<Money> Create(long amountCents, string currency)
    {
        if (amountCents < 0)
            return Result.Failure<Money>(new Error("Codes.Money.NegativeAmount", "AmountCents cannot be negative."));

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            return Result.Failure<Money>(
                new Error("Codes.Money.InvalidCurrency", "Currency must be a 3-letter ISO-4217 code.")
            );

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (!normalizedCurrency.All(char.IsLetter))
            return Result.Failure<Money>(
                new Error("Codes.Money.InvalidCurrency", "Currency must contain only letters.")
            );

        return Result.Success(new Money(amountCents, normalizedCurrency));
    }

    public static Result<Money> Zero(string currency) => Create(0, currency);

    public Result<Money> Add(Money other)
    {
        if (!HasSameCurrency(other))
            return CurrencyMismatch();

        try
        {
            return Result.Success(new Money(checked(AmountCents + other.AmountCents), Currency));
        }
        catch (OverflowException)
        {
            return Result.Failure<Money>(new Error("Codes.Money.Overflow", "The monetary operation overflowed."));
        }
    }

    public Result<Money> Subtract(Money other)
    {
        if (!HasSameCurrency(other))
            return CurrencyMismatch();

        if (other.AmountCents > AmountCents)
            return Result.Failure<Money>(
                new Error("Codes.Money.NegativeResult", "The monetary result cannot be negative.")
            );

        return Result.Success(new Money(AmountCents - other.AmountCents, Currency));
    }

    public Result<Money> Min(Money other)
    {
        if (!HasSameCurrency(other))
            return CurrencyMismatch();

        return Result.Success(AmountCents <= other.AmountCents ? this : other);
    }

    private bool HasSameCurrency(Money other) =>
        string.Equals(Currency, other.Currency, StringComparison.Ordinal);

    private static Result<Money> CurrencyMismatch() =>
        Result.Failure<Money>(
            new Error("Codes.Money.CurrencyMismatch", "Cannot operate on amounts in different currencies.")
        );
}
