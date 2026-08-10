using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Files;

public sealed record ChecksumSha256
{
    private static readonly Regex Sha256 = new("^[a-f0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private ChecksumSha256(string value) => Value = value;

    public string Value { get; }

    public static Result<ChecksumSha256> Create(string value) =>
        Sha256.IsMatch(value)
            ? Result.Success(new ChecksumSha256(value))
            : Result.Failure<ChecksumSha256>(FileErrors.InvalidChecksum);
}
