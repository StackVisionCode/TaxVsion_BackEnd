using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Domain;

/// <summary>
/// Paleta de marca por defecto de la empresa (Tenant_Branding_Colors_Plan.md §4) — único lugar del
/// código donde viven estos 4 valores. El frontend declara los mismos 4 hex como fallback estático en
/// <c>styles.scss</c> para evitar el flash de color equivocado antes de que responda la API; si estos
/// valores cambian, hay que actualizar ambos lados.
/// </summary>
public static class SystemBrandingDefaults
{
    public static readonly HexColor PrimaryColor = HexColor.Create("#1E466B").Value;
    public static readonly HexColor AccentColor = HexColor.Create("#67BAF4").Value;
    public static readonly HexColor BackgroundColor = HexColor.Create("#FAFAFA").Value;
    public static readonly HexColor TextColor = HexColor.Create("#0D0D0D").Value;
}
