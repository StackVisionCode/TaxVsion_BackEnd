using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Application.Sharing;

/// <summary>Fase C3 — links vigentes/pasados creados sobre un file puntual (para su dueno).</summary>
public sealed record ListShareLinksForFileQuery(Guid TenantId, StorageActorScope Scope, Guid FileId);

public static class ListShareLinksForFileHandler
{
    public static async Task<Result<IReadOnlyList<ShareLinkResponse>>> Handle(
        ListShareLinksForFileQuery query,
        IFileObjectRepository files,
        IShareLinkRepository shares,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(query.TenantId, query.FileId, ct);
        if (file is null || !query.Scope.CanAccess(file))
            return Result.Failure<IReadOnlyList<ShareLinkResponse>>(FileErrors.NotFound);

        var links = await shares.ListForResourceAsync(query.TenantId, query.FileId, ShareResourceType.File, ct);
        return Result.Success<IReadOnlyList<ShareLinkResponse>>(
            links.Select(link => ShareLinkResponseMapper.Map(link, clock.UtcNow)).ToArray()
        );
    }
}

/// <summary>Fase C4 — links vigentes/pasados creados sobre una folder puntual (para su dueno).</summary>
public sealed record ListShareLinksForFolderQuery(Guid TenantId, StorageActorScope Scope, Guid FolderId);

public static class ListShareLinksForFolderHandler
{
    public static async Task<Result<IReadOnlyList<ShareLinkResponse>>> Handle(
        ListShareLinksForFolderQuery query,
        IFolderRepository folders,
        IShareLinkRepository shares,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        var folder = await folders.GetAsync(query.TenantId, query.FolderId, ct);
        if (folder is null || !query.Scope.CanAccess(folder))
            return Result.Failure<IReadOnlyList<ShareLinkResponse>>(FolderErrors.NotFound);

        var links = await shares.ListForResourceAsync(query.TenantId, query.FolderId, ShareResourceType.Folder, ct);
        return Result.Success<IReadOnlyList<ShareLinkResponse>>(
            links.Select(link => ShareLinkResponseMapper.Map(link, clock.UtcNow)).ToArray()
        );
    }
}

/// <summary>
/// Fase C3 — links autenticados accesibles para el actor actual: TenantOnly +
/// SpecificUsers(yo) para un empleado/admin, o TenantCustomers(abierto o
/// restringido a mi CustomerId) para un actor de CustomerPortal. Public y
/// ExternalRecipients no aplican aca — no estan atados a una identidad autenticada.
/// </summary>
public sealed record ListSharedWithMeQuery(Guid TenantId, Guid ActorId, StorageActorScope Scope, int Skip, int Take);

public static class ListSharedWithMeHandler
{
    public static async Task<IReadOnlyList<ShareLinkResponse>> Handle(
        ListSharedWithMeQuery query,
        IShareLinkRepository shares,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 100);

        var links = query.Scope is { IsCustomerPortal: true, CustomerId: { } customerId }
            ? await shares.ListSharedWithCustomerAsync(query.TenantId, customerId, skip, take, ct)
            : await shares.ListSharedWithUserAsync(query.TenantId, query.ActorId, skip, take, ct);

        return links.Select(link => ShareLinkResponseMapper.Map(link, clock.UtcNow)).ToArray();
    }
}
