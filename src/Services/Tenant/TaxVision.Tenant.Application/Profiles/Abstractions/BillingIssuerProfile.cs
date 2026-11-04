namespace TaxVision.Tenant.Application.Profiles.Abstractions;

/// <summary>Un emisor tal como lo guarda Billing (valores viejos, posiblemente mal formados).</summary>
public sealed record BillingIssuerProfile(
    Guid TenantId,
    string? Name,
    string? TaxId,
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? Zip,
    string? Country,
    string? Phone,
    string? Email,
    string? Website,
    DateTime? UpdatedAtUtc
);
