using BuildingBlocks.Results;

namespace TaxVision.Documents.Domain.ValueObjects;

/// <summary>SHA-256 hex del contenido generado, para deduplicación/verificación.</summary>
public sealed record ContentHash
{
    public string Value { get; }

    private ContentHash(string value) => Value = value;

    public static Result<ContentHash> Create(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            return Result.Failure<ContentHash>(
                new Error("Documents.ContentHash.Invalid", "ContentHash must be a 64-character SHA-256 hex digest.")
            );

        return Result.Success(new ContentHash(normalized));
    }

    public override string ToString() => Value;
}
