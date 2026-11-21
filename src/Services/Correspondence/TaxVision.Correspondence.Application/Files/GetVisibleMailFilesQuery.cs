using BuildingBlocks.Contracts.Files;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Files;

/// <summary>
/// <paramref name="VisibleAccountIds"/> null = todos los buzones; <paramref name="RestrictToUserId"/> null = todos los
/// clientes. <paramref name="ViewerUserId"/> decide la marca de buzón personal; <paramref name="CanDownloadIncoming"/> =
/// tiene <c>correspondence.attachment.download</c>.
/// </summary>
public sealed record GetVisibleMailFilesQuery(
    Guid TenantId,
    IReadOnlyList<Guid> FileIds,
    IReadOnlyCollection<Guid>? VisibleAccountIds,
    Guid? RestrictToUserId,
    Guid ViewerUserId = default,
    bool CanDownloadIncoming = true,
    string? Purpose = null
);

/// <summary>
/// <c>POST /correspondence/files/visibility</c>: qué adjuntos de correo (enviados y entrantes ya descargados) ve quien
/// pregunta (File Library). Lo que no ve se omite; para descargar, también lo que no puede bajar.
/// </summary>
public static class GetVisibleMailFilesHandler
{
    public static async Task<FileVisibilityResponse> Handle(
        GetVisibleMailFilesQuery query,
        IMailFileVisibilityReader reader,
        CancellationToken ct
    )
    {
        var fileIds = query.FileIds.Distinct().ToList();
        // Sin archivos o sin ningún buzón visible no hay nada que mirar.
        if (fileIds.Count == 0 || query.VisibleAccountIds is { Count: 0 })
            return new FileVisibilityResponse([]);

        var items = await reader.GetVisibleFilesAsync(
            query.TenantId,
            fileIds,
            query.VisibleAccountIds,
            query.RestrictToUserId,
            query.ViewerUserId,
            query.CanDownloadIncoming,
            ct
        );
        return query.Purpose == FileVisibilityPurposes.Download
            ? new FileVisibilityResponse(items.Where(i => i.CanDownload).ToList())
            : new FileVisibilityResponse(items);
    }
}
