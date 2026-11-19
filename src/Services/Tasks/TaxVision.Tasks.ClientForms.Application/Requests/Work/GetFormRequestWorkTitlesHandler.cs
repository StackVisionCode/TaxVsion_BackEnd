using BuildingBlocks.Contracts.Work;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Work;

/// <summary>Lo pide <c>/tasks/work/titles</c> solo con permiso de form requests y el módulo disponible.</summary>
public sealed record GetFormRequestWorkTitlesQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    IReadOnlyList<Guid> Ids
);

/// <summary>Títulos en vivo de form requests con la visibilidad de la bandeja; lo que no se ve se omite.</summary>
public static class GetFormRequestWorkTitlesHandler
{
    public static async Task<IReadOnlyList<WorkTitle>> Handle(
        GetFormRequestWorkTitlesQuery query,
        IFormRequestWorkReader reader,
        CancellationToken ct
    )
    {
        if (query.Ids.Count == 0)
            return [];

        var titles = await reader.TitlesAsync(query.TenantId, query.Ids, query.ActorUserId, query.CanViewAll, ct);
        return
        [
            .. titles
                .Select(t => (Title: t, Clipped: WorkLimits.ClipTitle(t.Title)))
                .Where(x => x.Clipped is not null)
                .Select(x => x.Title with { Title = x.Clipped! }),
        ];
    }
}
