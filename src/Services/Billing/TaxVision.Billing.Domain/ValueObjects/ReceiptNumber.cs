using BuildingBlocks.Results;

namespace TaxVision.Billing.Domain.ValueObjects;

/// <summary>Número del comprobante, server-generado (p.ej. RCP-2026-000123).</summary>
public sealed record ReceiptNumber
{
    public string Value { get; }

    private ReceiptNumber(string value) => Value = value;

    public static Result<ReceiptNumber> Create(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 40)
            return Result.Failure<ReceiptNumber>(
                new Error("Billing.ReceiptNumber.Invalid", "ReceiptNumber is required and cannot exceed 40 characters.")
            );

        return Result.Success(new ReceiptNumber(normalized));
    }

    public override string ToString() => Value;
}
