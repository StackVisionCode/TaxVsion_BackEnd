using BuildingBlocks.Contracts.Files;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Application.Library;

/// <param name="UnavailableSources">Orígenes pedidos cuyo dueño no respondió en esta petición.</param>
/// <param name="PartialSources">Orígenes cuyo ámbito se cortó por el tope.</param>
public sealed record LibraryPage(
    IReadOnlyList<LibraryItemResponse> Items,
    string? NextCursor,
    IReadOnlyList<string> UnavailableSources,
    IReadOnlyList<string> PartialSources
);

/// <summary>Lo que necesita la lectura por tramos.</summary>
public sealed record LibraryPageDependencies(
    ILibraryReadRepository Repository,
    IFileOwnerVisibilityClient Owners,
    LibraryOwnerOptions Options,
    ISystemClock Clock
);

/// <summary>
/// Arma una página leyendo tramos del SQL. Las filas de un origen por fila solo entran si su dueño las confirma en
/// esta misma petición. El cursor apunta a la última fila examinada (aceptada o descartada), así que una página
/// puede venir corta con cursor. Sin orígenes por fila basta una vuelta con una fila de más. Si se pide un origen
/// que usa el ámbito de Communication, el ámbito se pide antes del primer tramo y su tiempo cuenta en el presupuesto.
/// </summary>
public static class LibraryPageAssembler
{
    public static Task<LibraryPage> ReadAsync(
        LibraryPageDependencies dependencies,
        Guid tenantId,
        LibraryAccess access,
        IReadOnlyList<LibrarySource> sources,
        LibraryFilter filter,
        CancellationToken ct
    ) => new PageWalk(dependencies, tenantId, access, sources, filter).RunAsync(ct);

    private sealed class PageWalk(
        LibraryPageDependencies dependencies,
        Guid tenantId,
        LibraryAccess access,
        IReadOnlyList<LibrarySource> sources,
        LibraryFilter filter
    )
    {
        private readonly List<LibraryItemResponse> _items = [];
        private readonly HashSet<FileOwnerService> _down = [];
        private LibraryCursor? _cursor = filter.Cursor;
        private CommunicationFileScope? _scope;
        private IReadOnlyList<string> _truncated = [];

        public async Task<LibraryPage> RunAsync(CancellationToken ct)
        {
            var options = dependencies.Options;
            var tranche = sources.Any(IsRowScoped) ? Math.Max(1, options.Window) : filter.Take + 1;
            var budget = TimeSpan.FromSeconds(Math.Max(0, options.OwnerBudgetSeconds));
            var maxRounds = Math.Max(1, options.MaxRounds);
            var (more, rounds, spent) = (true, 0, await ReadScopeAsync(ct));
            // El primer tramo se lee siempre: sin él no habría cursor que devolver.
            while (_items.Count < filter.Take && more && rounds < maxRounds && (rounds == 0 || spent < budget))
            {
                var origins = sources
                    .Where(source => OwnerOf(source) is not { } owner || !_down.Contains(owner))
                    .ToList();
                if (origins.Count == 0)
                    return Page(more: false);
                var rows = await dependencies.Repository.SearchAsync(
                    tenantId,
                    LibraryAccessPolicy.ScopeFor(access, origins, _scope),
                    filter.Continue(_cursor),
                    tranche,
                    ct
                );
                var started = dependencies.Clock.UtcNow;
                var answers = await AskOwnersAsync(rows, ct);
                spent += dependencies.Clock.UtcNow - started;
                _down.UnionWith(answers.Where(answer => !answer.Value.Available).Select(answer => answer.Key));
                more = await ExamineAsync(rows, answers, rows.Count == tranche, ct);
                rounds++;
            }
            return Page(more);
        }

        // Paso 0: una sola vez por petición y solo si se pide un origen con ámbito. Devuelve el tiempo gastado.
        private async Task<TimeSpan> ReadScopeAsync(CancellationToken ct)
        {
            if (!sources.Any(source => LibrarySourceCatalog.Get(source).UsesScope))
                return TimeSpan.Zero;
            var started = dependencies.Clock.UtcNow;
            var answer = await dependencies.Owners.GetCommunicationScopeAsync(ct);
            if (answer.Available)
                (_scope, _truncated) = (answer.Scope, answer.Scope.Truncated);
            else
                _down.Add(FileOwnerService.Communication);
            return dependencies.Clock.UtcNow - started;
        }

        // Recorre el tramo en orden; devuelve si queda algo por examinar después del cursor.
        private async Task<bool> ExamineAsync(
            IReadOnlyList<LibraryRow> rows,
            IReadOnlyDictionary<FileOwnerService, OwnerVisibility> answers,
            bool trancheWasFull,
            CancellationToken ct
        )
        {
            var folderPaths = await FolderPathsAsync(rows, ct);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                _cursor = LibraryCursor.After(filter.Sort, row.CreatedAtUtc, row.Name, row.Id);
                if (Accept(row, answers, folderPaths) is { } item)
                    _items.Add(item);
                if (_items.Count == filter.Take)
                    return trancheWasFull || i < rows.Count - 1;
            }
            return trancheWasFull;
        }

        // Una llamada por dueño y tramo, en paralelo; tasks y forms van juntos a Tasks.
        private async Task<IReadOnlyDictionary<FileOwnerService, OwnerVisibility>> AskOwnersAsync(
            IReadOnlyList<LibraryRow> rows,
            CancellationToken ct
        )
        {
            var byOwner = rows.Select(row => (Row: row, Owner: OwnerOfRow(row)))
                .Where(entry => entry.Owner is { } owner && !_down.Contains(owner))
                .GroupBy(entry => entry.Owner!.Value, entry => entry.Row.Id)
                .ToList();
            var answers = await Task.WhenAll(
                byOwner.Select(async group =>
                    (
                        Owner: group.Key,
                        Answer: await dependencies.Owners.CheckAsync(
                            group.Key,
                            group.Distinct().ToList(),
                            FileVisibilityPurposes.List,
                            ct
                        )
                    )
                )
            );
            return answers.ToDictionary(entry => entry.Owner, entry => entry.Answer);
        }

        private LibraryItemResponse? Accept(
            LibraryRow row,
            IReadOnlyDictionary<FileOwnerService, OwnerVisibility> answers,
            IReadOnlyDictionary<Guid, string> folderPaths
        )
        {
            // El SQL ya filtró por la lista blanca; una fila que no clasifica no se muestra.
            if (LibrarySourceCatalog.Classify(row) is not { } source)
                return null;
            // Origen con variantes: la fila exige la de su tipo de recurso.
            if (!LibraryAccessPolicy.Admits(access, source, row.SourceResourceType))
                return null;
            if (OwnerOf(source) is not { } owner)
                return ToItem(row, source, folderPaths, visible: null);
            // Nunca sin la confirmación del dueño en esta petición.
            return
                answers.TryGetValue(owner, out var answer)
                && answer.Available
                && answer.Visible.TryGetValue(row.Id, out var visible)
                ? ToItem(row, source, folderPaths, visible)
                : null;
        }

        private LibraryPage Page(bool more) =>
            new(
                _items,
                more && _cursor is not null ? _cursor.Encode() : null,
                sources
                    .Where(source => OwnerOf(source) is { } owner && _down.Contains(owner))
                    .Select(LibrarySourceCatalog.IdOf)
                    .ToList(),
                sources
                    .Where(source => LibrarySourceCatalog.Get(source).ScopeLists?.Any(_truncated.Contains) == true)
                    .Select(LibrarySourceCatalog.IdOf)
                    .ToList()
            );

        private async Task<IReadOnlyDictionary<Guid, string>> FolderPathsAsync(
            IReadOnlyList<LibraryRow> rows,
            CancellationToken ct
        )
        {
            var folderIds = rows.Where(row =>
                    LibrarySourceCatalog.Classify(row) == LibrarySource.Documents && row.FolderId is not null
                )
                .Select(row => row.FolderId!.Value)
                .Distinct()
                .ToList();
            return folderIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await dependencies.Repository.FolderPathsAsync(tenantId, folderIds, ct);
        }

        private LibraryItemResponse ToItem(
            LibraryRow row,
            LibrarySource source,
            IReadOnlyDictionary<Guid, string> folderPaths,
            VisibleFile? visible
        )
        {
            var folderPath =
                source == LibrarySource.Documents && row.FolderId is { } folderId
                    ? folderPaths.GetValueOrDefault(folderId)
                    : null;
            // El dueño puede negar la descarga de una fila que sí deja ver; la vista previa usa la misma URL.
            var canDownload = visible?.CanDownload ?? true;
            return new LibraryItemResponse(
                row.Id,
                row.Name,
                row.ContentType,
                row.SizeBytes,
                row.CreatedAtUtc,
                // Los PDF generados por el sistema llevan el tenant como autor.
                row.CreatedBy == tenantId
                    ? null
                    : row.CreatedBy,
                row.TaxYear,
                LibrarySourceCatalog.IdOf(source),
                LibrarySourceCatalog.KindOf(source, row.OwnerType, row.SourceRole, row.SourceResourceType),
                row.RelatedCustomerId,
                folderPath,
                visible?.Title,
                visible is null
                    ? LibrarySourceCatalog.OpenInOf(source, row.Id, row.SourceResourceId, row.RelatedCustomerId)
                    : LibrarySourceCatalog.OpenInOf(visible),
                LibraryAccessPolicy.CanPreview(access) && canDownload,
                LibraryAccessPolicy.CanDownload(access) && canDownload,
                LibrarySourceCatalog.PreviewOf(source, row.SourceRole),
                visible?.RestrictedTo
            );
        }

        private static bool IsRowScoped(LibrarySource source) => LibrarySourceCatalog.Get(source).RowScoped;

        private static FileOwnerService? OwnerOf(LibrarySource source) => LibrarySourceCatalog.Get(source).Owner;

        private static FileOwnerService? OwnerOfRow(LibraryRow row) =>
            LibrarySourceCatalog.Classify(row) is { } source ? OwnerOf(source) : null;
    }
}
