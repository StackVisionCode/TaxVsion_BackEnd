using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Application.Sharing;

/// <summary>
/// Fase C4 — resuelve si un ShareLink de tipo Folder cubre un file puntual,
/// recorriendo la cadena de carpetas padre EN TIEMPO DE ACCESO (nunca se
/// materializa una lista de "items cubiertos" al crear el link). Reglas:
/// - link.ResourceId debe ser la carpeta directa del file, o cualquier ancestro
///   si IsRecursive; si no es recursivo, solo el hijo directo cuenta.
/// - Si AppliesToFutureItems es false, el file debe haber existido ya cuando se
///   creo el link (CreatedAtUtc &lt;= link.CreatedAtUtc) — lo agregado despues
///   no queda cubierto por este link (ver ShareLinkFolderItemAddedIntegrationEvent,
///   que igual avisa de la adicion).
/// </summary>
internal static class FolderShareCoverage
{
    // Cota defensiva: ningun arbol real de carpetas navegables llega a esta profundidad.
    private const int MaxWalkDepth = 64;

    public static async Task<bool> CoversAsync(
        ShareLink link,
        FileObject file,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        if (link.ResourceType != ShareResourceType.Folder || file.FolderId is not { } startFolderId)
            return false;

        // FolderAssignedAtUtc (no CreatedAtUtc): lo que importa es cuando el file
        // entro a ESTA carpeta, no cuando se subio — puede haberse subido mucho
        // antes y recien moverse aca despues de creado el link.
        if (
            !link.AppliesToFutureItems
            && (file.FolderAssignedAtUtc is not { } assignedAtUtc || assignedAtUtc > link.CreatedAtUtc)
        )
            return false;

        var currentFolderId = (Guid?)startFolderId;
        for (var depth = 0; depth < MaxWalkDepth && currentFolderId is { } folderId; depth++)
        {
            if (folderId == link.ResourceId)
                return true;
            if (!link.IsRecursive)
                return false; // sin recursividad, solo el hijo directo (ya descartado arriba) cuenta

            var folder = await folders.GetAsync(link.TenantId, folderId, ct);
            currentFolderId = folder?.ParentFolderId;
        }

        return false;
    }
}
