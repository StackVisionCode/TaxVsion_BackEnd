namespace TaxVision.Tenant.Application.Profiles;

/// <summary>Perfil público de la oficina (GET/PUT <c>tenants/current/profile</c>).</summary>
public sealed record OfficeProfileResponse(
    string? LegalName,
    string? DisplayName,
    string? EntityType,
    string? Ein,
    string? Phone,
    string? Email,
    string? Website,
    OfficeAddressResponse PhysicalAddress,
    bool MailingSameAsPhysical,
    OfficeAddressResponse? MailingAddress,
    string DefaultLanguage,
    IReadOnlyList<BusinessHoursResponse> BusinessHours,
    string? BookingUrl,
    string? ReviewUrl,
    long Version,
    DateTime? UpdatedAtUtc,
    IReadOnlyList<OfficeClosureResponse>? Closures = null
);
