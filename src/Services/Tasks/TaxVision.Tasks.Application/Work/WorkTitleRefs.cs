using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;

namespace TaxVision.Tasks.Application.Work;

/// <summary>Lote de títulos en vivo: con lista y con el tope del contrato. Los tipos ajenos se ignoran después.</summary>
public static class WorkTitleRefs
{
    public static Result<IReadOnlyList<WorkTitleRef>> Validate(WorkTitlesRequest? request) =>
        request?.Items is not { } items || items.Count > WorkLimits.TitleBatch
            ? Result.Failure<IReadOnlyList<WorkTitleRef>>(WorkErrors.InvalidTitles)
            : Result.Success(items);

    /// <summary>Ids sin repetir de un tipo; las entradas nulas no cuentan.</summary>
    public static IReadOnlyList<Guid> IdsOf(IReadOnlyList<WorkTitleRef> refs, string resourceType) =>
        [.. refs.Where(r => r is not null && r.ResourceType == resourceType).Select(r => r.Id).Distinct()];

    /// <summary>Recorta cada título; el que queda vacío se omite.</summary>
    public static IReadOnlyList<WorkTitle> Clip(IEnumerable<WorkTitle> titles) =>
        [
            .. titles
                .Select(t => (Title: t, Clipped: WorkLimits.ClipTitle(t.Title)))
                .Where(x => x.Clipped is not null)
                .Select(x => x.Title with { Title = x.Clipped! }),
        ];
}

/// <summary>Página de la lista de trabajo: desde 1, tamaño por defecto y máximo del contrato.</summary>
public static class WorkPaging
{
    public static (int Page, int Size) Normalize(int? page, int? size) =>
        (
            page is > 0 ? page.Value : 1,
            size is > 0 ? Math.Min(size.Value, WorkLimits.ItemsMaxPageSize) : WorkLimits.ItemsPageSize
        );
}
