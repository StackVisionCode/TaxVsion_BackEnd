using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>
/// Correo del firmante externo. Se normaliza (trim + lowercase) en la construcción para
/// que las comparaciones (dedup por request, matching contra CustomerEmailProjection)
/// sean consistentes.
/// </summary>
public sealed record SignerEmail
{
    public const int MaxLength = 320; // RFC 5321 local-part + @ + domain

    public string Value { get; }

    private SignerEmail(string value) => Value = value;

    public static Result<SignerEmail> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<SignerEmail>(new Error("Signature.SignerEmail.Empty", "Signer email is required."));

        var normalized = candidate.Trim().ToLowerInvariant();
        if (normalized.Length > MaxLength)
            return Result.Failure<SignerEmail>(
                new Error("Signature.SignerEmail.Length", $"Signer email cannot exceed {MaxLength} characters.")
            );

        // Validación pragmática: exactamente un '@', local y dominio no vacíos, dominio con al menos un '.'.
        var atIndex = normalized.IndexOf('@');
        var lastAt = normalized.LastIndexOf('@');
        if (atIndex <= 0 || atIndex != lastAt || atIndex >= normalized.Length - 1)
            return Result.Failure<SignerEmail>(
                new Error("Signature.SignerEmail.Format", "Signer email format is invalid.")
            );

        var domain = normalized[(atIndex + 1)..];
        if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.'))
            return Result.Failure<SignerEmail>(
                new Error("Signature.SignerEmail.Format", "Signer email domain is invalid.")
            );

        return Result.Success(new SignerEmail(normalized));
    }

    public override string ToString() => Value;
}
