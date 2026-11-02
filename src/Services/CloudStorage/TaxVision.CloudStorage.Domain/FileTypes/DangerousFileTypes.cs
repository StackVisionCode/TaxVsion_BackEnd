namespace TaxVision.CloudStorage.Domain.FileTypes;

/// <summary>
/// Extensiones NUNCA permitidas, sin importar carpeta, plan ni catálogo (Fase L1.1): contenido pirateable,
/// ejecutables e imágenes de disco. Es un invariante de seguridad: vive en código y la configuración no lo
/// puede relajar; el catálogo de tipos rechaza darlas de alta.
/// </summary>
public static class DangerousFileTypes
{
    public static IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Video pirateable
            ".mkv", ".avi", ".mov", ".mpg", ".mpeg", ".flv", ".wmv", ".m4v",
            // Audio pirateable
            ".mp3", ".flac", ".wav", ".aac", ".ape", ".wma",
            // Ejecutables
            ".exe", ".dll", ".msi", ".bat", ".cmd", ".com", ".scr", ".ps1", ".vbs", ".jar", ".apk", ".ipa",
            // Imágenes de disco
            ".iso", ".img", ".dmg", ".vmdk", ".vhd",
            // Piracy
            ".torrent",
        };

    public static bool IsDangerous(string extension) => Extensions.Contains(extension);
}
