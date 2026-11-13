using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>Una respuesta de "How did you hear about us?". El código es estable; las etiquetas se pueden editar.</summary>
public sealed class BookingPageHeardAboutOption : BaseEntity
{
    public const int MaxLabelLength = 100;

    private BookingPageHeardAboutOption() { }

    public Guid BookingPageId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string LabelEn { get; private set; } = string.Empty;
    public string? LabelEs { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public bool AllowsDetail { get; private set; }

    internal static BookingPageHeardAboutOption Create(Guid bookingPageId, string code) =>
        new() { BookingPageId = bookingPageId, Code = code };

    internal void Apply(string labelEn, string? labelEs, int sortOrder, bool isActive, bool allowsDetail)
    {
        LabelEn = labelEn;
        LabelEs = labelEs;
        SortOrder = sortOrder;
        IsActive = isActive;
        AllowsDetail = allowsDetail;
    }

    public string LabelFor(BookingLanguage language) =>
        language == BookingLanguage.Es && LabelEs is not null ? LabelEs : LabelEn;
}
