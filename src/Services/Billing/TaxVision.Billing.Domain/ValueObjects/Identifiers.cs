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

/// <summary>SHA-256 hex (64) que sella un comprobante para verificación pública.</summary>
public sealed record VerificationHash
{
    public string Value { get; }

    private VerificationHash(string value) => Value = value;

    public static Result<VerificationHash> Create(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            return Result.Failure<VerificationHash>(
                new Error(
                    "Billing.VerificationHash.Invalid",
                    "VerificationHash must be a 64-character SHA-256 hex digest."
                )
            );

        return Result.Success(new VerificationHash(normalized));
    }

    public override string ToString() => Value;
}
