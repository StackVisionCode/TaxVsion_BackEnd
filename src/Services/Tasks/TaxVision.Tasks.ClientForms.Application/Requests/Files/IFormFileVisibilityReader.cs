namespace TaxVision.Tasks.ClientForms.Application.Requests.Files;

/// <summary>Un archivo de formulario visible, con el request desde el que se ve y la revisión que lo contiene.</summary>
public sealed record VisibleFormFile(Guid FileId, Guid RequestId, string? Title, Guid? CustomerId, int RevisionNumber);

/// <summary>
/// Qué archivos de formularios ve quien mira: adjunto disponible o PDF de una revisión de un request visible en la
/// bandeja. Un archivo, una fila.
/// </summary>
public interface IFormFileVisibilityReader
{
    Task<IReadOnlyList<VisibleFormFile>> VisibleFormFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        Guid actorUserId,
        bool canViewAll,
        CancellationToken ct
    );
}
