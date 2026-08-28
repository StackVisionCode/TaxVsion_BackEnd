namespace TaxVision.Tenant.Domain.Enums;

/// <summary>
/// Token de color por ROL (no por jerarquía "primario/secundario") — cada valor le dice al frontend
/// DÓNDE se usa el color, no solo qué tono es. Vocabulario cerrado: el CSS de ambos frontends solo
/// tiene dos canales tematizables (primary → indigo, accent → orange), así que guardar un tercer
/// color no tendría dónde pintarse. Se guarda como texto legible en la columna.
/// </summary>
public enum BrandColorToken
{
    Primary,
    Accent,
}
