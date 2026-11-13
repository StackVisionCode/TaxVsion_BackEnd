using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>Enlace de reunión del anfitrión (Meet, Zoom, Teams). Va como lugar de la cita, así que cabe en ella.</summary>
public sealed record MeetingUrl
{
    public string Value { get; }

    private MeetingUrl(string value) => Value = value;

    public static Result<MeetingUrl> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (
            trimmed.Length == 0
            || trimmed.Length > Location.MaxLength
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host)
        )
            return Result.Failure<MeetingUrl>(BookingPageErrors.MeetingUrlInvalid);

        return Result.Success(new MeetingUrl(trimmed));
    }
}
