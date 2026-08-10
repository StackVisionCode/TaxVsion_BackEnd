using BuildingBlocks.Results;

namespace TaxVision.Billing.Domain.ValueObjects;

/// <summary>Número comercial de la factura, server-assigned al emitir (p.ej. INV-20260722-001).</summary>
public sealed record InvoiceNumber
{
    public string Value { get; }

    private InvoiceNumber(string value) => Value = value;

    public static Result<InvoiceNumber> Create(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 40)
            return Result.Failure<InvoiceNumber>(
                new Error("Billing.InvoiceNumber.Invalid", "InvoiceNumber is required and cannot exceed 40 characters.")
            );

        return Result.Success(new InvoiceNumber(normalized));
    }

    public override string ToString() => Value;
}
