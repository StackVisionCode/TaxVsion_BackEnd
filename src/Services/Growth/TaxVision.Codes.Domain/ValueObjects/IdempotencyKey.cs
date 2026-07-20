using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.ValueObjects;

public sealed record IdempotencyKey
{
    public string Value { get; }

    private IdempotencyKey(string value) => Value = value;

    public static Result<IdempotencyKey> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<IdempotencyKey>(
                new Error("Codes.IdempotencyKey.Required", "IdempotencyKey is required.")
            );

        var normalized = value.Trim();
        if (normalized.Length > 200)
            return Result.Failure<IdempotencyKey>(
                new Error("Codes.IdempotencyKey.TooLong", "IdempotencyKey must be 200 characters or fewer.")
            );

        return Result.Success(new IdempotencyKey(normalized));
    }

    public override string ToString() => Value;
}
