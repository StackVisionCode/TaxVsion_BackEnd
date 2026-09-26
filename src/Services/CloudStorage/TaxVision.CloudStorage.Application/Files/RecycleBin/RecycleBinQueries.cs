using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Application.Files.RecycleBin;

/// <summary>
/// Lista la papelera del tenant: carpetas borradas primero (una entrada por carpeta, con su conteo de
/// archivos) y luego los archivos borrados individualmente. Los archivos que cayeron junto con su
/// carpeta NO se listan sueltos — se restauran/purgan con la carpeta.
/// </summary>
public sealed record GetRecycleBinQuery(Guid TenantId, int Skip, int Take);

public static class GetRecycleBinHandler
{
    public static async Task<IReadOnlyList<RecycleBinItemResponse>> Handle(
        GetRecycleBinQuery query,
        IFileObjectRepository files,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 100);

        // Carpetas primero (raíces borradas). El conteo de archivos por carpeta es una consulta por raíz;
        // la papelera es acotada/paginada, así que es barato.
        var folderRoots = await folders.ListSoftDeletedRootsAsync(query.TenantId, skip, take, ct);
        var folderEntries = new List<RecycleBinItemResponse>(folderRoots.Count);
        foreach (var root in folderRoots)
        {
            var fileCount = await files.CountByDeletedBatchAsync(query.TenantId, root.Id, ct);
            folderEntries.Add(RecycleBinItemMapper.MapFolder(root, fileCount));
        }

        // Con el cupo restante, archivos borrados sueltos.
        var remaining = take - folderEntries.Count;
        var looseFiles =
            remaining > 0
                ? (await files.ListSoftDeletedLooseAsync(query.TenantId, skip, remaining, ct)).Select(
                    RecycleBinItemMapper.Map
                )
                : [];

        return folderEntries.Concat(looseFiles).ToArray();
    }
}
