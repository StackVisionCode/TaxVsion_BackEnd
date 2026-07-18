using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Domain.ValueObjects;

/// <summary>Texto que aparece en el estado de cuenta del taxpayer — configurado por el
/// tenant, ej. "ACME*TAX". Restringido a lo que Stripe/most card networks aceptan: 5-22
/// caracteres, sin &lt;, &gt;, \, ', ", *.</summary>
public sealed record StatementDescriptor
{
    private static readonly char[] ForbiddenCharacters = ['<', '>', '\\', '\'', '"', '*'];

    public string Value { get; }

    private StatementDescriptor(string value) => Value = value;

    public static Result<StatementDescriptor> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<StatementDescriptor>(
                new Error("StatementDescriptor.Empty", "StatementDescriptor is required.")
            );

        var trimmed = value.Trim();

        if (trimmed.Length is < 5 or > 22)
            return Result.Failure<StatementDescriptor>(
                new Error(
                    "StatementDescriptor.InvalidLength",
                    "StatementDescriptor must be between 5 and 22 characters."
                )
            );

        if (trimmed.IndexOfAny(ForbiddenCharacters) >= 0)
            return Result.Failure<StatementDescriptor>(
                new Error(
                    "StatementDescriptor.InvalidCharacters",
                    "StatementDescriptor contains characters not accepted by card networks."
                )
            );

        return Result.Success(new StatementDescriptor(trimmed));
    }

    public override string ToString() => Value;
}
