namespace TaxVision.CloudStorage.Domain.FileTypes;

/// <summary>Política ya resuelta para un upload puntual: tamaño máximo y tipos admitidos.</summary>
public sealed record EffectiveUploadPolicy(
    long MaxFileSizeBytes,
    IReadOnlyCollection<string> AllowedExtensions,
    IReadOnlyCollection<string> AllowedContentTypes
);

/// <summary>Lo que vende el plan del tenant en archivos (proyección de entitlements de Subscription).</summary>
public sealed record PlanFileAllowance(IReadOnlySet<string> FileTypeGroups, long MaxFileBytes);

/// <summary>Restricción que la oficina pone a lo que suben sus clientes desde el portal (null = sin restricción).</summary>
public sealed record PortalUploadRestriction(IReadOnlySet<string>? FileTypeGroups, long? MaxFileBytes);

/// <summary>
/// La regla, en un solo lugar: extensiones de la carpeta que existen y están habilitadas en el catálogo,
/// recortadas por los grupos del plan (si la carpeta lo usa) y por la restricción del portal (si el que
/// sube es un cliente), menos la lista negra. Tamaño: la excepción de BD manda; si no hay, el menor entre
/// carpeta y plan (si la carpeta usa el del plan); el portal solo puede bajarlo.
/// </summary>
public static class EffectiveUploadPolicyCalculator
{
    public static EffectiveUploadPolicy Calculate(
        FolderFilePolicy folder,
        IReadOnlyCollection<FileTypeDefinition> catalog,
        PlanFileAllowance plan,
        long? sizeOverrideBytes,
        PortalUploadRestriction? portal
    )
    {
        var definitions = catalog
            .Where(definition => definition.IsEnabled)
            .ToDictionary(definition => definition.Extension, StringComparer.OrdinalIgnoreCase);

        var allowed = folder
            .ExtensionList.Where(extension => !DangerousFileTypes.IsDangerous(extension))
            .Select(extension => definitions.GetValueOrDefault(extension))
            .OfType<FileTypeDefinition>()
            .Where(definition => !folder.UsesPlanFileTypes || plan.FileTypeGroups.Contains(definition.Group))
            .Where(definition => portal?.FileTypeGroups is null || portal.FileTypeGroups.Contains(definition.Group))
            .ToList();

        var maxBytes =
            sizeOverrideBytes
            ?? (folder.UsesPlanFileSize ? Math.Min(folder.MaxSizeBytes, plan.MaxFileBytes) : folder.MaxSizeBytes);
        if (portal?.MaxFileBytes is { } portalMax)
            maxBytes = Math.Min(maxBytes, portalMax);

        return new EffectiveUploadPolicy(
            maxBytes,
            allowed.Select(definition => definition.Extension).ToArray(),
            allowed
                .SelectMany(definition => definition.ContentTypeList)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        );
    }
}
