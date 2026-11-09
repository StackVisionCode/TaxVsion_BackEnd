using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Sharing;

/// <summary>
/// Fase C4 — crea un link de compartir sobre una Folder completa. IsRecursive
/// decide si cubre todo el subarbol o solo el contenido directo; AppliesToFutureItems
/// decide si lo agregado despues de crear el link queda cubierto automaticamente
/// (ver FolderShareCoverage, que aplica ambas reglas en tiempo de acceso).
/// </summary>
public sealed record CreateFolderShareLinkCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    bool ActorHasManagePermission,
    Guid FolderId,
    ShareVisibility Visibility,
    SharePermission Permission,
    string? Password,
    DateTime? ExpiresAtUtc,
    int? MaxAccessCount,
    bool IsRecursive,
    bool AppliesToFutureItems,
    IReadOnlyList<Guid> RecipientUserIds,
    IReadOnlyList<Guid> RecipientCustomerIds,
    IReadOnlyList<string> RecipientEmails,
    RequestAuditContext Audit
);

public static class CreateFolderShareLinkHandler
{
    public static async Task<Result<CreatedShareLinkResponse>> Handle(
        CreateFolderShareLinkCommand command,
        IFolderRepository folders,
        IShareLinkRepository shares,
        IStorageLimitRepository limits,
        IShareLinkPasswordHasher passwordHasher,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var folder = await folders.GetAsync(command.TenantId, command.FolderId, ct);
        if (folder is null || !command.Scope.CanAccess(folder))
            return Result.Failure<CreatedShareLinkResponse>(FolderErrors.NotFound);

        return await ShareLinkCreationCore.CreateAndPersist(
            command.TenantId,
            command.ActorId,
            command.ActorHasManagePermission,
            command.FolderId,
            ShareResourceType.Folder,
            command.Visibility,
            command.Permission,
            command.Password,
            command.ExpiresAtUtc,
            command.MaxAccessCount,
            command.IsRecursive,
            command.AppliesToFutureItems,
            command.RecipientUserIds,
            command.RecipientCustomerIds,
            command.RecipientEmails,
            folder.Name,
            "En",
            command.Audit,
            shares,
            limits,
            passwordHasher,
            audit,
            clock,
            bus,
            unitOfWork,
            ct
        );
    }
}
