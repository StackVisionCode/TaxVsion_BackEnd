using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Library.Queries;

/// <param name="Scope">Alcance general ya resuelto: la emisión vuelve a pasar la compuerta por id.</param>
public sealed record IssueLibraryDownloadUrlQuery(
    Guid TenantId,
    Guid ActorId,
    LibraryAccess Access,
    StorageActorScope Scope,
    Guid FileId,
    RequestAuditContext Audit
);

/// <summary>
/// URL de vista previa y descarga de la biblioteca: solo de un archivo que la misma búsqueda le devolvería a quien
/// llama. La emisión, la auditoría y la vigencia son las del endpoint general.
/// </summary>
public static class IssueLibraryDownloadUrlHandler
{
    public static async Task<Result<DownloadUrlResponse>> Handle(
        IssueLibraryDownloadUrlQuery query,
        ILibraryReadRepository library,
        IFileOwnerVisibilityClient owners,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        // No distingue "no existe" de "no lo ves".
        if (query.Access.Readable.Count == 0)
            return Result.Failure<DownloadUrlResponse>(FileErrors.NotFound);
        var scope = LibraryAccessPolicy.ScopeFor(query.Access, query.Access.Readable);
        if (await library.FindAsync(query.TenantId, scope, query.FileId, ct) is not { } row)
            return Result.Failure<DownloadUrlResponse>(FileErrors.NotFound);
        var issueScope = await ScopeForIssueAsync(owners, query, row, ct);
        if (issueScope.IsFailure)
            return Result.Failure<DownloadUrlResponse>(issueScope.Error);

        // Siempre como adjunto: el visor del CRM descarga los bytes y los pinta.
        return await bus.InvokeAsync<Result<DownloadUrlResponse>>(
            new IssueDownloadUrlQuery(
                query.TenantId,
                query.ActorId,
                issueScope.Value,
                query.FileId,
                query.Audit,
                ForPreview: false
            ),
            ct
        );
    }

    // Origen por fila: el dueño vuelve a decidir al emitir la URL; sin respuesta no hay URL. Lo que confirma puede
    // ser un archivo mediado por su dueño: con esa confirmación la compuerta de mediados no aplica a esta emisión.
    private static async Task<Result<StorageActorScope>> ScopeForIssueAsync(
        IFileOwnerVisibilityClient owners,
        IssueLibraryDownloadUrlQuery query,
        LibraryRow row,
        CancellationToken ct
    )
    {
        if (LibrarySourceCatalog.Classify(row) is not { } source)
            return Result.Failure<StorageActorScope>(FileErrors.NotFound);
        if (!LibraryAccessPolicy.Admits(query.Access, source, row.SourceResourceType))
            return Result.Failure<StorageActorScope>(FileErrors.NotFound);
        if (LibrarySourceCatalog.Get(source).Owner is not { } owner)
            return Result.Success(query.Scope);
        var answer = await owners.CheckAsync(owner, [row.Id], FileVisibilityPurposes.Download, ct);
        if (!answer.Available)
            return Result.Failure<StorageActorScope>(LibraryErrors.SourceUnavailable);
        return answer.Visible.TryGetValue(row.Id, out var visible) && visible.CanDownload
            ? Result.Success(query.Scope with { HidesOwnerMediated = false })
            : Result.Failure<StorageActorScope>(FileErrors.NotFound);
    }
}
