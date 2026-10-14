using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.CustomerAccess;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Domain.CustomerAccess;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>
/// Fase C2 — subcarpetas + archivos directamente dentro de folderId (null = raiz del owner
/// visible para el scope). OwnerType/OwnerId son un filtro opcional adicional
/// solo relevante para staff interno navegando la raiz de un tenant con muchos duenos
/// mezclados (Tenant + N Customers) — cierra el gap de "dame solo el arbol de este
/// cliente". El portal de cliente ya estaba y sigue estando acotado por
/// Scope.IsCustomerPortal/CustomerId, que gana siempre sobre estos dos filtros.
/// </summary>
public sealed record GetFolderContentsQuery(
    Guid TenantId,
    StorageActorScope Scope,
    Guid? FolderId,
    OwnerType? OwnerType = null,
    Guid? OwnerId = null,
    // Paginación opcional (carpetas primero). Take null = todo el contenido (compat hacia atrás).
    int Skip = 0,
    int? Take = null,
    // Filtro + orden server-side. Si tiene file-filters (año/extensión/estado) las carpetas se ocultan.
    FolderContentsFilter? Filter = null
);

public static class GetFolderContentsHandler
{
    public static async Task<FolderContentsResponse> Handle(
        GetFolderContentsQuery query,
        IFolderRepository folders,
        IFileObjectRepository files,
        IShareLinkRepository shares,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        var restrictedCustomerId = query.Scope.IsCustomerPortal ? query.Scope.CustomerId ?? Guid.Empty : (Guid?)null;
        var filter = query.Filter ?? FolderContentsFilter.None;
        // ABAC: solo cuando el resolver cargó el set (staff sin view_all con el flag on); null = sin filtro.
        var assignedCustomerIds = query.Scope.BypassAssignmentFilter ? null : query.Scope.AssignedCustomerIds;

        // Portal con reglas deny per-item: traemos sin paginar y filtramos+paginamos acá para que los
        // counts reflejen la vista real del cliente (sin esto el "load more" calcularía mal el total).
        if (query.Scope.HasAnyPortalDenyRules)
            return await HandlePortalWithDenyRulesAsync(
                query,
                folders,
                files,
                shares,
                clock,
                filter,
                restrictedCustomerId,
                assignedCustomerIds,
                ct
            );

        // Con un filtro de archivo activo (año/extensión/estado) las carpetas se ocultan: no pueden
        // satisfacer un criterio de archivo, y mostrarlas ensuciaría los totales de paginación.
        var folderCount = filter.HasFileFilters
            ? 0
            : await folders.CountSubfoldersAsync(
                query.TenantId,
                query.FolderId,
                restrictedCustomerId,
                assignedCustomerIds,
                query.OwnerType,
                query.OwnerId,
                ct
            );
        var fileCount = await files.CountInFolderAsync(
            query.TenantId,
            query.FolderId,
            restrictedCustomerId,
            assignedCustomerIds,
            query.OwnerType,
            query.OwnerId,
            filter,
            ct
        );

        // Ventana "carpetas primero": la página cubre primero las subcarpetas y, si sobra cupo, sigue
        // con archivos. Con Take null se trae todo (sin paginar).
        int skip = query.Skip < 0 ? 0 : query.Skip;
        int? folderTake = query.Take;
        int folderSkip = query.Take is null ? 0 : Math.Min(skip, folderCount);
        if (query.Take is { } take)
            folderTake = Math.Min(take, Math.Max(0, folderCount - folderSkip));

        var subfolders = folderTake is 0
            ? []
            : await folders.ListSubfoldersAsync(
                query.TenantId,
                query.FolderId,
                restrictedCustomerId,
                assignedCustomerIds,
                query.OwnerType,
                query.OwnerId,
                filter,
                folderSkip,
                folderTake,
                ct
            );

        int? fileTake = query.Take;
        int fileSkip = query.Take is null ? 0 : Math.Max(0, skip - folderCount);
        if (query.Take is { } t)
            fileTake = Math.Max(0, t - subfolders.Count);

        var filesInFolder = fileTake is 0
            ? []
            : await files.ListInFolderAsync(
                query.TenantId,
                query.FolderId,
                restrictedCustomerId,
                assignedCustomerIds,
                query.OwnerType,
                query.OwnerId,
                filter,
                fileSkip,
                fileTake,
                ct
            );

        // Una sola consulta marca los que están compartidos (solo los items de esta página).
        var resourceIds = subfolders.Select(f => f.Id).Concat(filesInFolder.Select(f => f.Id)).ToArray();
        var shared =
            resourceIds.Length == 0
                ? new HashSet<Guid>()
                : (
                    await shares.ListResourceIdsWithActiveShareAsync(query.TenantId, resourceIds, clock.UtcNow, ct)
                ).ToHashSet();

        return new FolderContentsResponse(
            subfolders
                .Select(f => FolderResponseMapper.Map(f, query.Scope) with { IsShared = shared.Contains(f.Id) })
                .ToArray(),
            filesInFolder
                .Select(f => FileResponseMapper.Map(f, query.Scope) with { IsShared = shared.Contains(f.Id) })
                .ToArray(),
            folderCount,
            fileCount,
            folderCount + fileCount,
            skip,
            query.Take
        )
        {
            CustomerBaselineCapabilities = EffectiveCapabilities.ForTenantBaseline(query.Scope),
        };
    }

    private static async Task<FolderContentsResponse> HandlePortalWithDenyRulesAsync(
        GetFolderContentsQuery query,
        IFolderRepository folders,
        IFileObjectRepository files,
        IShareLinkRepository shares,
        ISystemClock clock,
        FolderContentsFilter filter,
        Guid? restrictedCustomerId,
        IReadOnlyCollection<Guid>? assignedCustomerIds,
        CancellationToken ct
    )
    {
        // Traemos la carpeta completa (sin take) y aplicamos el filtro deny antes de contar/paginar.
        var allSubfolders = filter.HasFileFilters
            ? Array.Empty<Folder>()
            : (
                await folders.ListSubfoldersAsync(
                    query.TenantId,
                    query.FolderId,
                    restrictedCustomerId,
                    assignedCustomerIds,
                    query.OwnerType,
                    query.OwnerId,
                    filter,
                    skip: null,
                    take: null,
                    ct
                )
            ).ToArray();
        var allFiles = (
            await files.ListInFolderAsync(
                query.TenantId,
                query.FolderId,
                restrictedCustomerId,
                assignedCustomerIds,
                query.OwnerType,
                query.OwnerId,
                filter,
                skip: null,
                take: null,
                ct
            )
        ).ToArray();

        var visibleFolders = allSubfolders
            .Where(f => !query.Scope.IsHiddenForPortal(AccessResourceType.Folder, f.Id))
            .ToArray();
        var visibleFiles = allFiles.Where(f => !query.Scope.IsHiddenForPortal(AccessResourceType.File, f.Id)).ToArray();

        var folderCount = visibleFolders.Length;
        var fileCount = visibleFiles.Length;

        var skip = query.Skip < 0 ? 0 : query.Skip;
        var folderSkip = query.Take is null ? 0 : Math.Min(skip, folderCount);
        int? folderTake = query.Take is null ? null : Math.Min(query.Take.Value, Math.Max(0, folderCount - folderSkip));
        var pagedFolders =
            folderTake is 0 ? Array.Empty<Folder>()
            : folderTake is null ? visibleFolders.Skip(folderSkip).ToArray()
            : visibleFolders.Skip(folderSkip).Take(folderTake.Value).ToArray();

        int fileSkip = query.Take is null ? 0 : Math.Max(0, skip - folderCount);
        int? fileTake = query.Take is null ? null : Math.Max(0, query.Take.Value - pagedFolders.Length);
        var pagedFiles =
            fileTake is 0 ? Array.Empty<FileObject>()
            : fileTake is null ? visibleFiles.Skip(fileSkip).ToArray()
            : visibleFiles.Skip(fileSkip).Take(fileTake.Value).ToArray();

        var resourceIds = pagedFolders.Select(f => f.Id).Concat(pagedFiles.Select(f => f.Id)).ToArray();
        var shared =
            resourceIds.Length == 0
                ? new HashSet<Guid>()
                : (
                    await shares.ListResourceIdsWithActiveShareAsync(query.TenantId, resourceIds, clock.UtcNow, ct)
                ).ToHashSet();

        return new FolderContentsResponse(
            pagedFolders
                .Select(f => FolderResponseMapper.Map(f, query.Scope) with { IsShared = shared.Contains(f.Id) })
                .ToArray(),
            pagedFiles
                .Select(f => FileResponseMapper.Map(f, query.Scope) with { IsShared = shared.Contains(f.Id) })
                .ToArray(),
            folderCount,
            fileCount,
            folderCount + fileCount,
            skip,
            query.Take
        )
        {
            CustomerBaselineCapabilities = EffectiveCapabilities.ForTenantBaseline(query.Scope),
        };
    }
}
