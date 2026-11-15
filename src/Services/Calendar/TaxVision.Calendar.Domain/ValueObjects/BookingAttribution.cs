using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>
/// Por dónde llegó el visitante: el botón del landing, los UTM de la campaña y solo el dominio del referer
/// (nunca la URL completa, que puede traer datos personales).
/// </summary>
public sealed partial record BookingAttribution
{
    public const int MaxUtmLength = 100;

    public BookingEntrySource EntrySource { get; }

    public string? UtmSource { get; }

    public string? UtmMedium { get; }

    public string? UtmCampaign { get; }

    public string? ReferrerDomain { get; }

    private BookingAttribution(
        BookingEntrySource entrySource,
        string? source,
        string? medium,
        string? campaign,
        string? referrer
    )
    {
        EntrySource = entrySource;
        UtmSource = source;
        UtmMedium = medium;
        UtmCampaign = campaign;
        ReferrerDomain = referrer;
    }

    public static Result<BookingAttribution> Create(
        BookingEntrySource entrySource,
        string? utmSource,
        string? utmMedium,
        string? utmCampaign,
        string? referrerDomain
    )
    {
        var source = Clean(utmSource);
        var medium = Clean(utmMedium);
        var campaign = Clean(utmCampaign);
        var referrer = string.IsNullOrWhiteSpace(referrerDomain) ? null : referrerDomain.Trim().ToLowerInvariant();
        if (!Enum.IsDefined(entrySource) || source.Invalid || medium.Invalid || campaign.Invalid)
            return Result.Failure<BookingAttribution>(PublicBookingErrors.AttributionInvalid);
        if (referrer is not null && !DomainPattern().IsMatch(referrer))
            return Result.Failure<BookingAttribution>(PublicBookingErrors.AttributionInvalid);

        return Result.Success(
            new BookingAttribution(entrySource, source.Value, medium.Value, campaign.Value, referrer)
        );
    }

    private static (string? Value, bool Invalid) Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, false);
        var trimmed = value.Trim();
        return (trimmed, trimmed.Length > MaxUtmLength || PlainText.HasControlCharacters(trimmed));
    }

    [GeneratedRegex("^(?=.{1,253}$)([a-z0-9-]{1,63}\\.)+[a-z]{2,63}$")]
    private static partial Regex DomainPattern();
}
