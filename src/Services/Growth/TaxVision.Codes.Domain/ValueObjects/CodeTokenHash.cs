using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.ValueObjects;

public sealed record CodeTokenHash
{
    public string Value { get; }

    private CodeTokenHash(string value) => Value = value;

    public static Result<CodeTokenHash> Create(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            return Result.Failure<CodeTokenHash>(
                new Error("Codes.CodeTokenHash.Invalid", "CodeTokenHash must be a 64-character SHA-256 hex digest.")
            );

        return Result.Success(new CodeTokenHash(normalized));
    }

    public override string ToString() => Value;
}
