namespace TaxVision.Tenant.Application.Profiles;

/// <summary>
/// Lo que el cliente del portal ve de su oficina (GET <c>tenants/current/client-profile</c>). Solo datos
/// públicos: nunca EIN, nombre legal, tipo de entidad ni cumplimiento.
/// </summary>
public sealed record ClientOfficeProfileResponse(
    string Name,
    string? Phone,
    string? Email,
    string? Website,
    OfficeAddressResponse? Address,
    string TimeZoneId,
    IReadOnlyList<BusinessHoursResponse> BusinessHours,
    string? BookingUrl,
    string? ReviewUrl
);
