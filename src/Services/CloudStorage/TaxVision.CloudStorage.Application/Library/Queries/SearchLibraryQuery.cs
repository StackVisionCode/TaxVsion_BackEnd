using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Application.Library.Queries;

public sealed record SearchLibraryQuery(Guid TenantId, LibraryAccess Access, LibraryFilter Filter);

/// <summary>
/// Búsqueda de la biblioteca: una página de lo autorizado (los orígenes por fila, confirmados por su dueño) y, si
/// se piden, los conteos por origen.
/// </summary>
public static class SearchLibraryHandler
{
    public static async Task<LibrarySearchResponse> Handle(
        SearchLibraryQuery query,
        ILibraryReadRepository library,
        IFileOwnerVisibilityClient owners,
        IOptions<LibraryOwnerOptions> options,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        // Sin ningún origen legible no hay nada que leer.
        if (query.Access.Readable.Count == 0)
            return new LibrarySearchResponse([], null, new LibraryCounts(0, []), [], []);

        var sources = LibraryAccessPolicy.Requested(query.Access, query.Filter.Sources);
        var page =
            sources.Count == 0
                ? new LibraryPage([], null, [], [])
                : await LibraryPageAssembler.ReadAsync(
                    new LibraryPageDependencies(library, owners, options.Value, clock),
                    query.TenantId,
                    query.Access,
                    sources,
                    query.Filter,
                    ct
                );
        var counts = query.Filter.IncludeCounts ? await CountAsync(query, library, ct) : null;
        return new LibrarySearchResponse(
            page.Items,
            page.NextCursor,
            counts,
            page.UnavailableSources,
            page.PartialSources
        );
    }

    // Con todos los filtros menos el de origen, sobre cada origen legible (también los que dan 0). Los orígenes
    // por fila no se cuentan: haría falta preguntar al dueño por cada candidato.
    private static async Task<LibraryCounts> CountAsync(
        SearchLibraryQuery query,
        ILibraryReadRepository library,
        CancellationToken ct
    )
    {
        var readable = LibraryAccessPolicy.Requested(query.Access, []);
        var sqlSources = readable.Where(source => !LibrarySourceCatalog.Get(source).RowScoped).ToList();
        var counted =
            sqlSources.Count == 0
                ? new Dictionary<LibrarySource, int>()
                : await library.CountBySourceAsync(
                    query.TenantId,
                    LibraryAccessPolicy.ScopeFor(query.Access, sqlSources),
                    query.Filter,
                    ct
                );
        var bySource = readable
            .Select(source => new LibrarySourceCount(
                LibrarySourceCatalog.IdOf(source),
                LibrarySourceCatalog.Get(source).RowScoped ? null : counted.GetValueOrDefault(source)
            ))
            .ToList();
        int? total = bySource.Any(count => count.Count is null) ? null : bySource.Sum(count => count.Count!.Value);
        return new LibraryCounts(total, bySource);
    }
}
