using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.ValueObjects;

public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    /// <summary>El monto en centavos, como lo guardan las intenciones y lo cobra PaymentApp.</summary>
    public long Cents => (long)(Amount * 100m);

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

    /// <summary>Monto que ya viene en centavos, del catálogo o de la base: la moneda se conserva como está.</summary>
    public static Money FromCents(long cents, string currency) => new(cents / 100m, currency);

    public static Money Zero(string currency) => new(0m, currency.Trim().ToUpperInvariant());
}
