using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.ValueObjects;

public sealed record CodeDisplay
{
    public string Prefix { get; }
    public string LastFour { get; }

    private CodeDisplay(string prefix, string lastFour)
    {
        Prefix = prefix;
        LastFour = lastFour;
    }

    public static Result<CodeDisplay> FromToken(string codeToken)
    {
        var normalized = codeToken?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length is < 8 or > 200)
            return Result.Failure<CodeDisplay>(
                new Error(
                    "Codes.CodeDisplay.InvalidToken",
                    "Code token must contain between 8 and 200 characters."
                )
            );

        return Create(
            normalized[..4].ToUpperInvariant(),
            normalized[^4..].ToUpperInvariant()
        );
    }

    public static Result<CodeDisplay> Create(string prefix, string lastFour)
    {
        var normalizedPrefix = prefix?.Trim().ToUpperInvariant();
        var normalizedLastFour = lastFour?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalizedPrefix) || normalizedPrefix.Length > 12)
            return Result.Failure<CodeDisplay>(
                new Error("Codes.CodeDisplay.InvalidPrefix", "Prefix must contain between 1 and 12 characters.")
            );

        if (string.IsNullOrWhiteSpace(normalizedLastFour) || normalizedLastFour.Length != 4)
            return Result.Failure<CodeDisplay>(
                new Error("Codes.CodeDisplay.InvalidLastFour", "LastFour must contain exactly 4 characters.")
            );

        return Result.Success(new CodeDisplay(normalizedPrefix, normalizedLastFour));
    }

    public override string ToString() => $"{Prefix}...{LastFour}";
}
