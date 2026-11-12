using TaxVision.Tenant.Domain.Profiles;

namespace TaxVision.Tenant.Api.Contracts;

/// <summary>
/// Estado completo del perfil (mismo cuerpo que el GET sin <c>version</c>/<c>updatedAtUtc</c>). Todo es
/// opcional; <c>mailingSameAsPhysical</c> ausente = true; <c>closures</c> ausente = no tocar los guardados.
/// </summary>
public sealed record UpdateOfficeProfileRequest(
    string? LegalName,
    string? DisplayName,
    string? EntityType,
    string? Ein,
    string? Phone,
    string? Email,
    string? Website,
    OfficeAddressRequest? PhysicalAddress,
    bool? MailingSameAsPhysical,
    OfficeAddressRequest? MailingAddress,
    string? DefaultLanguage,
    IReadOnlyList<BusinessHoursRequest>? BusinessHours,
    string? BookingUrl,
    string? ReviewUrl,
    IReadOnlyList<OfficeClosureRequest>? Closures = null
)
{
    public OfficeProfileDraft ToDraft() =>
        new(
            LegalName,
            DisplayName,
            EntityType,
            Ein,
            Phone,
            Email,
            Website,
            PhysicalAddress?.ToDraft(),
            MailingSameAsPhysical ?? true,
            MailingAddress?.ToDraft(),
            DefaultLanguage,
            BusinessHours?.Select(hours => hours.ToDraft()).ToList(),
            BookingUrl,
            ReviewUrl,
            Closures?.Select(closure => closure.ToDraft()).ToList()
        );
}
