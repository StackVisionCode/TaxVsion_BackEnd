using BuildingBlocks.Results;

namespace TaxVision.Billing.Domain.ValueObjects;

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
