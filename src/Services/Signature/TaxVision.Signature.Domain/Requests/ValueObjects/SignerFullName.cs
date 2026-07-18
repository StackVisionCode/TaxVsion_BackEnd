using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>
/// Nombre completo del firmante tal como aparece en el documento y en el Certificate
/// of Completion. Se preserva la capitalización original (a diferencia de
/// <see cref="SignerEmail"/>) porque el nombre es una unidad de presentación.
/// </summary>
public sealed record SignerFullName
{
    public const int MinLength = 2;
    public const int MaxLength = 200;

    public string Value { get; }

    private SignerFullName(string value) => Value = value;

    public static Result<SignerFullName> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<SignerFullName>(
                new Error("Signature.SignerFullName.Empty", "Signer full name is required.")
            );

        var trimmed = candidate.Trim();
        if (trimmed.Length is < MinLength or > MaxLength)
            return Result.Failure<SignerFullName>(
                new Error(
                    "Signature.SignerFullName.Length",
                    $"Signer full name must be between {MinLength} and {MaxLength} characters."
                )
            );

        return Result.Success(new SignerFullName(trimmed));
    }

    public override string ToString() => Value;
}
