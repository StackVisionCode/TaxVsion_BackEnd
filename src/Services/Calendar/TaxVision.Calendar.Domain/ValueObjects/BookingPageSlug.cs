using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>Nombre corto y público de una página de reservas (p. ej. "demo"): minúsculas, números y guiones.</summary>
public sealed partial record BookingPageSlug
{
    public string Value { get; }

    private BookingPageSlug(string value) => Value = value;

    public static Result<BookingPageSlug> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return Pattern().IsMatch(trimmed)
            ? Result.Success(new BookingPageSlug(trimmed))
            : Result.Failure<BookingPageSlug>(BookingPageErrors.SlugInvalid);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,38}[a-z0-9]$")]
    private static partial Regex Pattern();
}
