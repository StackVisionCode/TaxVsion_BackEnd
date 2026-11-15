using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>Nombre del visitante, en una línea.</summary>
public sealed record ProspectName
{
    public const int MaxLength = 200;

    public string Value { get; }

    private ProspectName(string value) => Value = value;

    public static Result<ProspectName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<ProspectName>(PublicBookingErrors.NameRequired);

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
            return Result.Failure<ProspectName>(PublicBookingErrors.NameTooLong);
        return PlainText.HasControlCharacters(trimmed, allowLineBreaks: false)
            ? Result.Failure<ProspectName>(PublicBookingErrors.TextInvalid)
            : Result.Success(new ProspectName(trimmed));
    }
}
