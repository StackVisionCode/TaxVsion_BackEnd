using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>Texto en inglés (obligatorio, idioma por defecto) y, si se quiere, en español.</summary>
public sealed record LocalizedText
{
    public string En { get; }

    public string? Es { get; }

    private LocalizedText(string en, string? es)
    {
        En = en;
        Es = es;
    }

    public static Result<LocalizedText> Create(string? en, string? es, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(en))
            return Result.Failure<LocalizedText>(BookingPageErrors.TextRequired);

        var english = en.Trim();
        var spanish = string.IsNullOrWhiteSpace(es) ? null : es.Trim();
        if (english.Length > maxLength || spanish?.Length > maxLength)
            return Result.Failure<LocalizedText>(BookingPageErrors.TextTooLong);

        return Result.Success(new LocalizedText(english, spanish));
    }

    /// <summary>Los dos idiomas caben en el tope (el VO se pudo crear con otro).</summary>
    public bool Fits(int maxLength) => En.Length <= maxLength && (Es is null || Es.Length <= maxLength);

    /// <summary>El texto en el idioma pedido; sin traducción, el inglés.</summary>
    public string For(BookingLanguage language) => language == BookingLanguage.Es && Es is not null ? Es : En;
}
