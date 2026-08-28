namespace TaxVision.Tenant.Domain.Enums;

/// <summary>
/// Tipo de archivo de marca. Vocabulario cerrado: un logo y un favicon por superficie. Permite que
/// un tenant tenga dos logos y dos favicons distintos (uno por superficie), porque cada superficie
/// es una <see cref="BrandSurface"/> con su propia colección de assets. Se guarda como texto.
/// </summary>
public enum BrandAssetKey
{
    Logo,
    Favicon,
}
