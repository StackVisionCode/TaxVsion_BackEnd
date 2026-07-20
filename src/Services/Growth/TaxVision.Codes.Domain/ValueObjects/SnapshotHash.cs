using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.ValueObjects;

public sealed record SnapshotHash
{
    public string Value { get; }

    private SnapshotHash(string value) => Value = value;

    public static Result<SnapshotHash> Create(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            return Result.Failure<SnapshotHash>(
                new Error("Codes.SnapshotHash.Invalid", "SnapshotHash must be a 64-character SHA-256 hex digest.")
            );

        return Result.Success(new SnapshotHash(normalized));
    }

    public override string ToString() => Value;
}
