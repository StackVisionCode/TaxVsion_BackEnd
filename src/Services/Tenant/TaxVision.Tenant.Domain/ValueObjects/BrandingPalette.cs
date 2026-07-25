namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// Paleta de marca ya resuelta (custom o default de la empresa, nunca parcial) — lo que
/// <see cref="Tenant.ResolveBrandingPalette"/> siempre devuelve. El frontend nunca recibe un color
/// vacío: si el tenant no personalizó un campo, viaja el default de <see cref="SystemBrandingDefaults"/>
/// en su lugar (Tenant_Branding_Colors_Plan.md §3.3).
/// </summary>
public sealed record BrandingPalette(
    HexColor PrimaryColor,
    HexColor AccentColor,
    HexColor BackgroundColor,
    HexColor TextColor,
    bool IsCustomized
);
