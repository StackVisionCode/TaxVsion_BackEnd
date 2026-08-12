using BuildingBlocks.Results;

namespace TaxVision.Inventory.Domain.ValueObjects;

/// <summary>Monto + moneda (ISO 4217). Copia local — Inventory es independiente de Catalog (no comparte
/// dominio). Se persiste como owned type.</summary>
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
            return Result.Failure<Money>(InventoryErrors.InvalidAmount);

        var normalized = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsLetter))
            return Result.Failure<Money>(InventoryErrors.InvalidCurrency);

        return Result.Success(new Money(amount, normalized));
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
