namespace TaxVision.CloudStorage.Application.Library.Queries;

public sealed record GetLibrarySourcesQuery(LibraryAccess Access);

/// <summary>Orígenes que quien llama puede leer. No toca la base.</summary>
public static class GetLibrarySourcesHandler
{
    public static LibrarySourcesResponse Handle(GetLibrarySourcesQuery query)
    {
        var canPreview = LibraryAccessPolicy.CanPreview(query.Access);
        var canDownload = LibraryAccessPolicy.CanDownload(query.Access);
        var sources = LibraryAccessPolicy
            .Requested(query.Access, [])
            .Select(LibrarySourceCatalog.Get)
            .Select(definition => new LibrarySourceCapability(
                definition.Id,
                definition.RowScoped,
                canPreview,
                canDownload
            ))
            .ToList();
        return new LibrarySourcesResponse(sources, LibraryAccessPolicy.AssignmentRestricted(query.Access));
    }
}
