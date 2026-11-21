using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Persistence;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Files;

/// <summary>Lo pide <c>/tasks/files/visibility</c> solo con permiso de form requests y el módulo disponible.</summary>
public sealed record GetVisibleFormFilesQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    IReadOnlyList<Guid> FileIds,
    string Purpose
);

/// <summary>
/// Archivos de formularios que quien mira ve en el drawer del request. Antes de una descarga deja "answers viewed",
/// la misma huella que abrir el detalle.
/// </summary>
public static class GetVisibleFormFilesHandler
{
    public static async Task<IReadOnlyList<VisibleFile>> Handle(
        GetVisibleFormFilesQuery query,
        IFormFileVisibilityReader reader,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (query.FileIds.Count == 0)
            return [];

        var files = await reader.VisibleFormFilesAsync(
            query.TenantId,
            query.FileIds,
            query.ActorUserId,
            query.CanViewAll,
            ct
        );
        if (query.Purpose == FileVisibilityPurposes.Download && files.Count > 0)
            await RecordAnswersViewedAsync(query, files, audit, unitOfWork, ct);
        return [.. files.Select(ToVisibleFile)];
    }

    // Una vez por request, con la revisión más alta de las pedidas.
    private static async Task RecordAnswersViewedAsync(
        GetVisibleFormFilesQuery query,
        IReadOnlyList<VisibleFormFile> files,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        foreach (var request in files.GroupBy(file => file.RequestId))
            await audit.RecordAnswersViewedAsync(
                query.TenantId,
                request.Key,
                request.Max(file => file.RevisionNumber),
                query.ActorUserId,
                nowUtc,
                ct
            );
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static VisibleFile ToVisibleFile(VisibleFormFile file) =>
        new(
            file.FileId,
            WorkResourceTypes.FormRequest,
            file.RequestId,
            WorkLimits.ClipTitle(file.Title),
            file.CustomerId
        );
}
