namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>Una respuesta de "How did you hear about us?" tal como la pide la configuración.</summary>
public sealed record HeardAboutOptionInput(
    string? Code,
    string? LabelEn,
    string? LabelEs,
    int SortOrder,
    bool IsActive,
    bool AllowsDetail
);
