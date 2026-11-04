using TaxVision.Tenant.Domain.Profiles;

namespace TaxVision.Tenant.Api.Contracts;

/// <summary>
/// Estado completo del perfil (mismo cuerpo que el GET sin <c>version</c>/<c>updatedAtUtc</c>). Todo es
/// opcional; <c>mailingSameAsPhysical</c> ausente = true.
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
    string? ReviewUrl
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
            ReviewUrl
        );
}
