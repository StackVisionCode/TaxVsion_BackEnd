using BuildingBlocks.Results;

namespace TaxVision.Documents.Domain.ValueObjects;

/// <summary>Tipo de documento (Invoice, Receipt, CreditNote, …). String extensible — Documents no
/// codifica reglas fiscales por tipo, solo lo usa para resolver plantilla/validación estructural.</summary>
public sealed record DocumentType
{
    public string Value { get; }

    private DocumentType(string value) => Value = value;

    public static Result<DocumentType> Create(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 60)
            return Result.Failure<DocumentType>(
                new Error("Documents.DocumentType.Invalid", "DocumentType is required and cannot exceed 60 characters.")
            );

        return Result.Success(new DocumentType(normalized));
    }

    public override string ToString() => Value;
}
