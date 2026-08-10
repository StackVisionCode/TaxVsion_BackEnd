using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Files;

public sealed record ObjectKey
{
    private static readonly Regex SafeKey = new(
        @"^[a-zA-Z0-9][a-zA-Z0-9._/-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private ObjectKey(string value) => Value = value;

    public string Value { get; }

    public static Result<ObjectKey> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024)
            return Result.Failure<ObjectKey>(FileErrors.InvalidObjectKey);

        if (
            value.StartsWith('/')
            || value.EndsWith('/')
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains("//", StringComparison.Ordinal)
            || value.Contains('\\')
            || !SafeKey.IsMatch(value)
        )
            return Result.Failure<ObjectKey>(FileErrors.InvalidObjectKey);

        return Result.Success(new ObjectKey(value));
    }
}
