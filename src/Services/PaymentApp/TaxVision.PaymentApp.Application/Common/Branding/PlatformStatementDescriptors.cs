using BuildingBlocks.Platform;

namespace TaxVision.PaymentApp.Application.Common.Branding;

/// <summary>
/// Texto del estado de cuenta de la tarjeta: <c>{MARCA} {CONCEPTO}</c> (ej. "TAXOFFIX SAAS"). La marca sale
/// de PlatformSettings, en mayúsculas y solo letras y números (lo que aceptan las redes de tarjetas), y se
/// recorta para que el total no pase de 22 caracteres.
/// </summary>
public static class PlatformStatementDescriptors
{
    private const int MaxLength = 22;

    public static string For(PlatformSettingsSnapshot platform, string concept)
    {
        var brand = new string(platform.BrandName.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        var room = MaxLength - concept.Length - 1;
        if (brand.Length > room)
            brand = brand[..room];
        return brand.Length == 0 ? concept : $"{brand} {concept}";
    }
}
