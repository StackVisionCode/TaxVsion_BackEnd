namespace TaxVision.Notification.Application.Common;

/// <summary>URLs públicas del frontend para construir enlaces en los correos.</summary>
public sealed class PortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>Base de la app privada, p. ej. https://app.taxvision.com</summary>
    public string BaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>Dominio base usado para enlaces específicos de cada tenant.</summary>
    public string BaseDomain { get; set; } = "taxprocore.com";

    public string ProductName { get; set; } = "TaxVision";
}
