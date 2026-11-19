using BuildingBlocks.Contracts.Work;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Application.Files.Queries;

/// <summary><paramref name="Scope"/> ya resuelto con el ABAC de asignación de quien mira.</summary>
public sealed record GetWorkTitlesQuery(Guid TenantId, StorageActorScope Scope, IReadOnlyList<WorkTitleRef> Items);

/// <summary>
/// Títulos en vivo del historial (<c>POST /storage/work/titles</c>): nombre de cada <c>document</c> que quien mira
/// podría abrir (misma regla que leer el archivo). Omite lo borrado, lo ajeno y los tipos que no son de este servicio.
/// </summary>
public static class GetWorkTitlesHandler
{
    public static async Task<WorkTitlesResponse> Handle(
        GetWorkTitlesQuery query,
        IFileObjectRepository files,
        CancellationToken ct
    )
    {
        var ids = query
            .Items.Where(i => i.ResourceType == WorkResourceTypes.Document)
            .Select(i => i.Id)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return new WorkTitlesResponse([]);

        var visible = (await files.ListLiveByIdsAsync(query.TenantId, ids, ct))
            .Where(query.Scope.CanAccess)
            .ToDictionary(f => f.Id, f => f.OriginalName);

        var titles = new List<WorkTitle>();
        foreach (var id in ids)
            if (WorkLimits.ClipTitle(visible.GetValueOrDefault(id)) is { } title)
                titles.Add(new WorkTitle(WorkResourceTypes.Document, id, title));
        return new WorkTitlesResponse(titles);
    }
}
