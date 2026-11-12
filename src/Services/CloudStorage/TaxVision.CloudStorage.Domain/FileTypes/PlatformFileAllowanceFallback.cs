namespace TaxVision.CloudStorage.Domain.FileTypes;

/// <summary>
/// Respaldo de plataforma cuando la oficina no trae <c>storage.file_type_groups</c> /
/// <c>storage.max_file_bytes</c> (foto incompleta o tenant de plataforma sin suscripción). No depende de ningún
/// plan: es el mínimo, nunca "lo más amplio" por omisión.
/// </summary>
public static class PlatformFileAllowanceFallback
{
    public const string FileTypeGroups = "documents,spreadsheets,images,data,media";
    public const long MaxFileBytes = 10L * 1024 * 1024;

    public static IReadOnlySet<string> ParseGroups(string? csv) =>
        (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(group => group.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
}
