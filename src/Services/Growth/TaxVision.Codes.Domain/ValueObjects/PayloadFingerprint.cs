using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.ValueObjects;

public sealed record PayloadFingerprint
{
    public string Value { get; }

    private PayloadFingerprint(string value) => Value = value;

    public static Result<PayloadFingerprint> Create(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            return Result.Failure<PayloadFingerprint>(
                new Error(
                    "Codes.PayloadFingerprint.Invalid",
                    "PayloadFingerprint must be a 64-character SHA-256 hex digest."
                )
            );

        return Result.Success(new PayloadFingerprint(normalized));
    }

    public override string ToString() => Value;
}
