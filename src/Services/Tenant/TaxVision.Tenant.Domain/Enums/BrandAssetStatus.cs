namespace TaxVision.Tenant.Domain.Enums;

/// <summary>
/// Estado del ciclo de vida de un asset de marca. Reemplaza el truco implícito del modelo viejo
/// (donde <c>LogoUpdatedAtUtc == null</c> significaba "pendiente de escaneo"): aquí es explícito.
/// Un asset <see cref="Pending"/> aún no pasó el antivirus de CloudStorage y NUNCA debe servirse;
/// solo <see cref="Confirmed"/> es visible. Se guarda como texto.
/// </summary>
public enum BrandAssetStatus
{
    Pending,
    Confirmed,
}
