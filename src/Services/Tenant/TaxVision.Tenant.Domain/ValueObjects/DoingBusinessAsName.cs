using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// Nombre comercial (DBA) que la oficina muestra al público. La Pub 3112 del IRS prohíbe usar "IRS" en
/// el nombre de un proveedor de e-file, así que se rechaza como palabra suelta (no dentro de otra).
/// </summary>
public sealed partial record DoingBusinessAsName
{
    public const int MaxLength = 200;

    public static readonly Error TooLong = new(
        "Tenant.Profile.DisplayName",
        $"The business name (DBA) cannot exceed {MaxLength} characters."
    );

    public static readonly Error MentionsIrs = new(
        "Tenant.Profile.DisplayName",
        "The business name (DBA) cannot include \"IRS\" or \"Internal Revenue Service\"."
    );

    public string Value { get; }

    private DoingBusinessAsName(string value) => Value = value;

    public static Result<DoingBusinessAsName?> CreateOptional(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Success<DoingBusinessAsName?>(null);

        if (trimmed.Length > MaxLength)
            return Result.Failure<DoingBusinessAsName?>(TooLong);

        return IrsPattern().IsMatch(trimmed)
            ? Result.Failure<DoingBusinessAsName?>(MentionsIrs)
            : Result.Success<DoingBusinessAsName?>(new(trimmed));
    }

    public override string ToString() => Value;

    [GeneratedRegex(
        @"\bIRS\b|\bInternal\s+Revenue\s+Service\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex IrsPattern();
}
