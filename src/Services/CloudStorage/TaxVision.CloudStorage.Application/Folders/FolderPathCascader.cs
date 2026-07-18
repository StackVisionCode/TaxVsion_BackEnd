using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>
/// Fase C2 — cuando una carpeta se renombra o se mueve, su RelativePath
/// (materializado) cambia, y todo su subarbol tiene ese path como prefijo: hay
/// que reescribirselo a cada descendiente. Un solo lugar para esto, reutilizado
/// por RenameFolderHandler y MoveFolderHandler, evita reimplementar la misma
/// logica de cascada dos veces.
/// </summary>
internal static class FolderPathCascader
{
    public static async Task CascadeAsync(
        Guid tenantId,
        string oldPathPrefix,
        string newPathPrefix,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        var descendants = await folders.ListByPathPrefixAsync(tenantId, oldPathPrefix, ct);
        foreach (var descendant in descendants)
        {
            var suffix = descendant.RelativePath[oldPathPrefix.Length..];
            descendant.RebasePath(newPathPrefix + suffix);
        }
    }
}
