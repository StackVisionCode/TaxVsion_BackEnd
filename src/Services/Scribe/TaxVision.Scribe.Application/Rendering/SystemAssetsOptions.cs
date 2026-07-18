namespace TaxVision.Scribe.Application.Rendering;

/// <summary>Assets propios de la plataforma (hoy solo el logo de fallback). Sección de config Scribe:SystemAssets.</summary>
public sealed class SystemAssetsOptions
{
    public const string SectionName = "Scribe:SystemAssets";

    public Guid HeaderLogoFileId { get; set; }
    public string HeaderLogoContentType { get; set; } = "image/png";
    public long HeaderLogoSizeBytes { get; set; }
}
