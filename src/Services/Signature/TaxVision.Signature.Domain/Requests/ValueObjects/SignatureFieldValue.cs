using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>
/// Texto que el firmante escribe en un campo <c>Text</c> del documento (p.ej. "Cónyuge",
/// un número de cuenta, una aclaración). Es evidencia del acto de firma, no parte del
/// template del campo: se valida y normaliza aquí antes de anclarse al aggregate.
///
/// <para>
/// Se recortan los espacios de los bordes y los caracteres de control (excepto el salto de
/// línea, que un textarea puede producir legítimamente) para que lo que se sella en el PDF
/// sea exactamente lo que se guarda, sin bytes invisibles.
/// </para>
/// </summary>
public sealed record SignatureFieldValue
{
    public const int MaxLength = 500;

    public string Value { get; }

    private SignatureFieldValue(string value) => Value = value;

    public static Result<SignatureFieldValue> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<SignatureFieldValue>(
                new Error("Signature.FieldValue.Empty", "The field value is required.")
            );

        var sanitized = Sanitize(candidate);
        if (sanitized.Length == 0)
            return Result.Failure<SignatureFieldValue>(
                new Error("Signature.FieldValue.Empty", "The field value is required.")
            );

        if (sanitized.Length > MaxLength)
            return Result.Failure<SignatureFieldValue>(
                new Error("Signature.FieldValue.Length", $"The field value cannot exceed {MaxLength} characters.")
            );

        return Result.Success(new SignatureFieldValue(sanitized));
    }

    private static string Sanitize(string candidate)
    {
        var kept = candidate.Where(c => c == '\n' || !char.IsControl(c)).ToArray();
        return new string(kept).Trim();
    }

    public override string ToString() => Value;
}
