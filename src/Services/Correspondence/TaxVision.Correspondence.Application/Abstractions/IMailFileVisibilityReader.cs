using BuildingBlocks.Contracts.Files;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Adjuntos de correos enviados que quien mira vería en Sent o en una conversación (File Library). Mismas reglas que
/// Mail: gate de buzón (<c>null</c> = todos) y cliente asignado (<c>null</c> = todos). Lo que no se ve se omite.
/// </summary>
public interface IMailFileVisibilityReader
{
    Task<IReadOnlyList<VisibleFile>> GetVisibleFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId,
        Guid viewerUserId,
        bool canDownloadIncoming,
        CancellationToken ct = default
    );
}
