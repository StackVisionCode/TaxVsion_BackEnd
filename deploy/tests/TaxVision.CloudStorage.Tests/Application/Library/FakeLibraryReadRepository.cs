using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application.Library;

/// <summary>Repositorio en memoria: clasifica con el catálogo y aplica la restricción por cliente como el SQL.</summary>
internal sealed class FakeLibraryReadRepository : ILibraryReadRepository
{
    private readonly List<(Guid TenantId, LibraryRow Row)> _rows = [];
    private readonly Dictionary<Guid, string> _folders = [];

    public int Calls { get; private set; }
    public List<int> RequestedTakes { get; } = [];
    public List<LibraryReadScope> RequestedScopes { get; } = [];

    public FakeLibraryReadRepository Add(Guid tenantId, LibraryRow row)
    {
        _rows.Add((tenantId, row));
        return this;
    }

    public FakeLibraryReadRepository Folder(Guid folderId, string path)
    {
        _folders[folderId] = path;
        return this;
    }

    public Task<IReadOnlyList<LibraryRow>> SearchAsync(
        Guid tenantId,
        LibraryReadScope scope,
        LibraryFilter filter,
        int take,
        CancellationToken ct
    )
    {
        Calls++;
        RequestedTakes.Add(take);
        RequestedScopes.Add(scope);
        IReadOnlyList<LibraryRow> rows = Visible(tenantId, scope)
            .Where(row => After(row, filter.Cursor))
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(take)
            .ToList();
        return Task.FromResult(rows);
    }

    public Task<LibraryRow?> FindAsync(Guid tenantId, LibraryReadScope scope, Guid fileId, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Visible(tenantId, scope).FirstOrDefault(row => row.Id == fileId));
    }

    public Task<IReadOnlyDictionary<LibrarySource, int>> CountBySourceAsync(
        Guid tenantId,
        LibraryReadScope scope,
        LibraryFilter filter,
        CancellationToken ct
    )
    {
        Calls++;
        RequestedScopes.Add(scope);
        IReadOnlyDictionary<LibrarySource, int> counts = Visible(tenantId, scope)
            .GroupBy(row => SourceOf(row)!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        return Task.FromResult(counts);
    }

    public Task<IReadOnlyDictionary<Guid, string>> FolderPathsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> folderIds,
        CancellationToken ct
    )
    {
        IReadOnlyDictionary<Guid, string> paths = _folders
            .Where(folder => folderIds.Contains(folder.Key))
            .ToDictionary(folder => folder.Key, folder => folder.Value);
        return Task.FromResult(paths);
    }

    // Solo el orden por defecto (newest), como el keyset del SQL.
    private static bool After(LibraryRow row, LibraryCursor? cursor) =>
        cursor is null
        || row.CreatedAtUtc < cursor.CreatedAtKey
        || (row.CreatedAtUtc == cursor.CreatedAtKey && row.Id.CompareTo(cursor.Id) < 0);

    private IEnumerable<LibraryRow> Visible(Guid tenantId, LibraryReadScope scope) =>
        _rows
            .Where(entry => entry.TenantId == tenantId)
            .Select(entry => entry.Row)
            .Where(row =>
                SourceOf(row) is { } source
                && scope.Sources.FirstOrDefault(s => s.Source == source) is { } sourceScope
                && (!sourceScope.Restricted || Admitted(row, source, scope.AssignedCustomerIds))
            );

    private static bool Admitted(LibraryRow row, LibrarySource source, IReadOnlyCollection<Guid> assigned) =>
        source == LibrarySource.Documents
            ? row.OwnerType == OwnerType.Tenant || (row.OwnerId is { } owner && assigned.Contains(owner))
            : row.RelatedCustomerId is { } customer && assigned.Contains(customer);

    private static LibrarySource? SourceOf(LibraryRow row) =>
        LibrarySourceCatalog.Classify(row.OwnerType, row.FolderType, row.SourceResourceType, row.SourceRole);

    internal static LibraryRow Row(
        OwnerType owner,
        FolderType folder,
        Guid? ownerId = null,
        string? resourceType = null,
        string? role = null,
        Guid? relatedCustomerId = null,
        DateTime? createdAtUtc = null,
        Guid? createdBy = null,
        Guid? folderId = null,
        Guid? resourceId = null
    ) =>
        new(
            Guid.NewGuid(),
            "file.pdf",
            "application/pdf",
            10,
            createdAtUtc ?? DateTime.UtcNow,
            createdBy ?? Guid.NewGuid(),
            2025,
            owner,
            ownerId,
            folder,
            folderId,
            resourceType,
            resourceId ?? (resourceType is null ? null : Guid.NewGuid()),
            role,
            relatedCustomerId ?? (owner == OwnerType.Customer ? ownerId : null)
        );
}
