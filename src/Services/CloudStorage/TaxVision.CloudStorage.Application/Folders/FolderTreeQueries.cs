using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>
/// 2026-07-20 — arbol COMPLETO de carpetas (sidebar expandible), a diferencia de
/// GetFolderContentsQuery que trae un nivel por vez para navegacion por clicks. OwnerType/
/// OwnerId null = todo el tenant (solo valido para staff interno; el portal de cliente
/// jamas ve mezclado el arbol de otro dueno, ver GetFolderTreeHandler).
/// </summary>
public sealed record GetFolderTreeQuery(
    Guid TenantId,
    StorageActorScope Scope,
    OwnerType? OwnerType = null,
    Guid? OwnerId = null
);

public sealed record FolderTreeNode(
    Guid Id,
    string Name,
    string RelativePath,
    string? Category,
    IReadOnlyList<FolderTreeNode> Children
);

public static class GetFolderTreeHandler
{
    public static async Task<Result<IReadOnlyList<FolderTreeNode>>> Handle(
        GetFolderTreeQuery query,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        var scopeResult = ResolveEffectiveOwnerScope(query);
        if (scopeResult.IsFailure)
            return Result.Failure<IReadOnlyList<FolderTreeNode>>(scopeResult.Error);

        var flat = await folders.ListAllForOwnerScopeAsync(
            query.TenantId,
            scopeResult.Value.OwnerType,
            scopeResult.Value.OwnerId,
            ct
        );
        return Result.Success(BuildTree(flat, parentId: null));
    }

    /// <summary>
    /// Portal de cliente: jamas se confia en lo que el caller pida para OwnerType/OwnerId —
    /// el propio Scope (derivado del JWT) ya define su alcance real, mismo criterio que
    /// Login/ForgotPassword ignorando el TenantId del body (ver README §26). Un cliente
    /// pidiendo explicitamente otro dueno (u otro OwnerType) es rechazado, no silenciosamente
    /// re-acotado — para que el llamador se entere de que su request estaba mal formado en
    /// vez de recibir datos de otro alcance sin darse cuenta.
    /// </summary>
    private static Result<OwnerScope> ResolveEffectiveOwnerScope(GetFolderTreeQuery query)
    {
        if (!query.Scope.IsCustomerPortal)
            return Result.Success(new OwnerScope(query.OwnerType, query.OwnerId));

        var customerId = query.Scope.CustomerId;
        if (customerId is null)
            return Result.Failure<OwnerScope>(FolderErrors.Forbidden);
        if (query.OwnerType is not null && query.OwnerType != OwnerType.Customer)
            return Result.Failure<OwnerScope>(FolderErrors.Forbidden);
        if (query.OwnerId is not null && query.OwnerId != customerId)
            return Result.Failure<OwnerScope>(FolderErrors.Forbidden);

        return Result.Success(new OwnerScope(OwnerType.Customer, customerId));
    }

    private static IReadOnlyList<FolderTreeNode> BuildTree(IReadOnlyList<Folder> all, Guid? parentId) =>
        all.Where(folder => folder.ParentFolderId == parentId)
            .OrderBy(folder => folder.Name, StringComparer.Ordinal)
            .Select(folder => new FolderTreeNode(
                folder.Id,
                folder.Name,
                folder.RelativePath,
                folder.Category,
                BuildTree(all, folder.Id)
            ))
            .ToArray();

    private sealed record OwnerScope(OwnerType? OwnerType, Guid? OwnerId);
}
