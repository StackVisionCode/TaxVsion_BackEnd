using BuildingBlocks.Results;

namespace TaxVision.Billing.Domain.ValueObjects;

/// <summary>Monto monetario en centavos (evita errores de redondeo de punto flotante).
/// Convención de plataforma: nunca <c>decimal</c> suelto en el dominio.</summary>
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
            return Result.Failure<Money>(new Error("Billing.Money.NegativeAmount", "AmountCents cannot be negative."));

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            return Result.Failure<Money>(
                new Error("Billing.Money.InvalidCurrency", "Currency must be a 3-letter ISO-4217 code.")
            );

        return Result.Success(new Money(amountCents, currency.Trim().ToUpperInvariant()));
    }

    public static Money Zero(string currency) => new(0L, currency.Trim().ToUpperInvariant());

    public Result<Money> Add(Money other) =>
        other.Currency != Currency
            ? Result.Failure<Money>(new Error("Billing.Money.CurrencyMismatch", "Cannot add different currencies."))
            : Result.Success(new Money(AmountCents + other.AmountCents, Currency));

    public Result<Money> Subtract(Money other)
    {
        if (other.Currency != Currency)
            return Result.Failure<Money>(
                new Error("Billing.Money.CurrencyMismatch", "Cannot subtract different currencies.")
            );

        if (other.AmountCents > AmountCents)
            return Result.Failure<Money>(new Error("Billing.Money.NegativeResult", "Result cannot be negative."));

        return Result.Success(new Money(AmountCents - other.AmountCents, Currency));
    }
}
