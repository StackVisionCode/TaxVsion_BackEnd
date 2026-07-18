namespace TaxVision.Connectors.Application.Common;

/// <summary>URL pública del frontend a la que redirige el callback de OAuth (D3 §12.4/12.5) tras un éxito o error — mismo patrón de config que Notification's PortalOptions.</summary>
public sealed class ConnectorsPortalOptions
{
    public const string SectionName = "Portal";

    public string BaseUrl { get; set; } = "http://localhost:4200";
}
