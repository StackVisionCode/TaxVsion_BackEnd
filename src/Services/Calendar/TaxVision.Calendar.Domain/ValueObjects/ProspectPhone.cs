using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>Teléfono opcional del visitante: dígitos y los separadores habituales, de 7 a 15 dígitos.</summary>
public sealed record ProspectPhone
{
    public const int MaxLength = 30;

    public string Value { get; }

    private ProspectPhone(string value) => Value = value;

    public static Result<ProspectPhone> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        var digits = 0;
        var allowed = true;
        foreach (var c in trimmed)
        {
            if (char.IsAsciiDigit(c))
                digits++;
            else if (c is not (' ' or '+' or '-' or '(' or ')' or '.'))
                allowed = false;
        }
        return trimmed.Length <= MaxLength && allowed && digits is >= 7 and <= 15
            ? Result.Success(new ProspectPhone(trimmed))
            : Result.Failure<ProspectPhone>(PublicBookingErrors.PhoneInvalid);
    }
}
