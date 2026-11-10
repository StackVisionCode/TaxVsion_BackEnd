namespace TaxVision.Tenant.Domain.Profiles;

/// <summary>
/// Estado completo del perfil tal como llega (sin validar). Todo es opcional. Con
/// <see cref="MailingSameAsPhysical"/> la dirección postal se ignora; una postal vacía también cuenta
/// como "igual a la física". <see cref="Closures"/> null = no tocar los cierres guardados (un cliente
/// que todavía no los conoce no los borra); una lista vacía los borra.
/// </summary>
public sealed record OfficeProfileDraft(
    string? LegalName,
    string? DisplayName,
    string? EntityType,
    string? Ein,
    string? Phone,
    string? Email,
    string? Website,
    OfficeAddressDraft? PhysicalAddress,
    bool MailingSameAsPhysical,
    OfficeAddressDraft? MailingAddress,
    string? DefaultLanguage,
    IReadOnlyList<BusinessHoursDraft>? BusinessHours,
    string? BookingUrl,
    string? ReviewUrl,
    IReadOnlyList<OfficeClosureDraft>? Closures = null
);
