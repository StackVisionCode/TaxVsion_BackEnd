namespace TaxVision.Documents.Application.Branding;

/// <summary>Configura (crea o actualiza) el perfil de marca del tenant. Idempotente: hay uno por tenant.
/// Los campos en null se guardan como "sin valor" (la factura usa el default para ese campo).</summary>
public sealed record UpsertDocumentBrandingCommand(
    Guid TenantId,
    string? DisplayName,
    string? LogoDataUri,
    string? BrandColorHex,
    string? FooterText
);

/// <summary>Consulta el perfil de marca del tenant.</summary>
public sealed record GetDocumentBrandingQuery(Guid TenantId);

/// <summary>Vista del perfil de marca (sin exponer RowVersion ni internals).</summary>
public sealed record DocumentBrandingDto(
    string? DisplayName,
    string? LogoDataUri,
    string? BrandColorHex,
    string? FooterText,
    DateTime UpdatedAtUtc
);
