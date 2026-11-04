namespace TaxVision.Tenant.Domain.Profiles;

/// <summary>Dirección tal como llega (sin validar). <c>Country</c> vacío = EE. UU.</summary>
public sealed record OfficeAddressDraft(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country
);
