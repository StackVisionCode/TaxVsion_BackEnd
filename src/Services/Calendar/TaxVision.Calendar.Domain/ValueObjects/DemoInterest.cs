using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>Qué quiere ver el visitante en la demo; texto plano.</summary>
public sealed record DemoInterest
{
    public const int MaxLength = 500;

    public string Value { get; }

    private DemoInterest(string value) => Value = value;

    public static Result<DemoInterest> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<DemoInterest>(PublicBookingErrors.InterestRequired);

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
            return Result.Failure<DemoInterest>(PublicBookingErrors.InterestTooLong);
        return PlainText.HasControlCharacters(trimmed, allowLineBreaks: true)
            ? Result.Failure<DemoInterest>(PublicBookingErrors.TextInvalid)
            : Result.Success(new DemoInterest(trimmed));
    }
}
