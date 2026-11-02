namespace TaxVision.CloudStorage.Domain.FileTypes;

/// <summary>
/// Lo que se asume cuando el plan del tenant no trae <c>storage.file_type_groups</c> /
/// <c>storage.max_file_bytes</c> (plan ad hoc o tenant de plataforma sin suscripción): el tier más chico
/// (Starter). Nunca se cae a "lo más amplio" por omisión.
/// </summary>
public static class PlanFileAllowanceDefaults
{
    public const string FileTypeGroups = "documents,spreadsheets,images,data,media";
    public const long MaxFileBytes = 10L * 1024 * 1024;

    public static IReadOnlySet<string> ParseGroups(string? csv) =>
        (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(group => group.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
}
