using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Billing.Domain.ValueObjects;

/// <summary>Número comercial de la factura, asignado por el servidor (INV-2026-00001, ONB-2026-00001).</summary>
public sealed partial record InvoiceNumber
{
    public string Value { get; }

    private InvoiceNumber(string value) => Value = value;

    public static Result<InvoiceNumber> Create(string value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 40 || !Format().IsMatch(normalized))
            return Result.Failure<InvoiceNumber>(
                new Error(
                    "Billing.InvoiceNumber.Invalid",
                    "InvoiceNumber must look like INV-2026-00001 and cannot exceed 40 characters."
                )
            );

        return Result.Success(new InvoiceNumber(normalized));
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"\A(INV|ONB)-[0-9]{4}-[0-9]{5,}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Format();
}
