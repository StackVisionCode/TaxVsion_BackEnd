using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Application.Files.RecycleBin;

/// <summary>Fase C1 — lista la papelera del tenant (FileStatus.SoftDeleted), sin filtrar por vencimiento.</summary>
public sealed record GetRecycleBinQuery(Guid TenantId, int Skip, int Take);

public static class GetRecycleBinHandler
{
    public static async Task<IReadOnlyList<RecycleBinItemResponse>> Handle(
        GetRecycleBinQuery query,
        IFileObjectRepository files,
        CancellationToken ct
    ) =>
        (await files.ListSoftDeletedAsync(query.TenantId, Math.Max(0, query.Skip), Math.Clamp(query.Take, 1, 100), ct))
            .Select(RecycleBinItemMapper.Map)
            .ToArray();
}
