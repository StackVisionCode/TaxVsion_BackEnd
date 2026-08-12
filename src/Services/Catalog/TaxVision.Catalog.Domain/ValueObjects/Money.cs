using BuildingBlocks.Results;

namespace TaxVision.Catalog.Domain.ValueObjects;

/// <summary>Monto + moneda (ISO 4217, 3 letras mayúsculas). Multi-moneda: cada precio/costo lleva su
/// moneda. Se persiste como owned type (columnas <c>*_Amount</c> / <c>*_Currency</c>).</summary>
public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Result<Money> Create(decimal amount, string? currency)
    {
        if (amount < 0)
            return Result.Failure<Money>(CatalogErrors.InvalidAmount);

        var normalized = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsLetter))
            return Result.Failure<Money>(CatalogErrors.InvalidCurrency);

        return Result.Success(new Money(amount, normalized));
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
