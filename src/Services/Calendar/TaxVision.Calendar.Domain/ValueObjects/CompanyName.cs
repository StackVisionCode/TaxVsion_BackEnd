using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>Empresa del visitante, en una línea.</summary>
public sealed record CompanyName
{
    public const int MaxLength = 200;

    public string Value { get; }

    private CompanyName(string value) => Value = value;

    public static Result<CompanyName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<CompanyName>(PublicBookingErrors.CompanyRequired);

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
            return Result.Failure<CompanyName>(PublicBookingErrors.CompanyTooLong);
        return PlainText.HasControlCharacters(trimmed, allowLineBreaks: false)
            ? Result.Failure<CompanyName>(PublicBookingErrors.TextInvalid)
            : Result.Success(new CompanyName(trimmed));
    }
}
