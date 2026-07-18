using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Domain.ValueObjects;

/// <summary>Monto monetario en centavos (evita errores de redondeo de punto flotante).</summary>
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
            return Result.Failure<Money>(new Error("Money.NegativeAmount", "AmountCents cannot be negative."));

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            return Result.Failure<Money>(
                new Error("Money.InvalidCurrency", "Currency must be a 3-letter ISO-4217 code.")
            );

        return Result.Success(new Money(amountCents, currency.Trim().ToUpperInvariant()));
    }

    public static Money Zero(string currency) => new(0L, currency.Trim().ToUpperInvariant());

    public Result<Money> Add(Money other)
    {
        if (other.Currency != Currency)
            return Result.Failure<Money>(
                new Error("Money.CurrencyMismatch", "Cannot add amounts in different currencies.")
            );

        return Result.Success(new Money(AmountCents + other.AmountCents, Currency));
    }

    public Result<Money> Subtract(Money other)
    {
        if (other.Currency != Currency)
            return Result.Failure<Money>(
                new Error("Money.CurrencyMismatch", "Cannot subtract amounts in different currencies.")
            );

        if (other.AmountCents > AmountCents)
            return Result.Failure<Money>(new Error("Money.NegativeResult", "Result cannot be negative."));

        return Result.Success(new Money(AmountCents - other.AmountCents, Currency));
    }
}
