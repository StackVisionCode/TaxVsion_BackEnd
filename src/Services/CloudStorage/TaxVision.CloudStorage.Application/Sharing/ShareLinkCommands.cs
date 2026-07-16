using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Sharing;

/// <summary>
/// Fase C3 — crea un link de compartir sobre un File. ActorHasManagePermission lo
/// resuelve el controller a partir del claim "perm" == cloudstorage.share.manage
/// — Upload/EditMetadata solo son otorgables por un actor con ese permiso.
/// </summary>
public sealed record CreateShareLinkCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    bool ActorHasManagePermission,
    Guid FileId,
    ShareVisibility Visibility,
    SharePermission Permission,
    string? Password,
    DateTime? ExpiresAtUtc,
    int? MaxAccessCount,
    IReadOnlyList<Guid> RecipientUserIds,
    IReadOnlyList<Guid> RecipientCustomerIds,
    IReadOnlyList<string> RecipientEmails,
    RequestAuditContext Audit
);

public static class CreateShareLinkHandler
{
    public static async Task<Result<CreatedShareLinkResponse>> Handle(
        CreateShareLinkCommand command,
        IFileObjectRepository files,
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
        var fileResult = await LoadShareableFile(command, files, ct);
        if (fileResult.IsFailure)
            return Result.Failure<CreatedShareLinkResponse>(fileResult.Error);

        return await ShareLinkCreationCore.CreateAndPersist(
            command.TenantId,
            command.ActorId,
            command.ActorHasManagePermission,
            command.FileId,
            ShareResourceType.File,
            command.Visibility,
            command.Permission,
            command.Password,
            command.ExpiresAtUtc,
            command.MaxAccessCount,
            isRecursive: false,
            appliesToFutureItems: false,
            command.RecipientUserIds,
            command.RecipientCustomerIds,
            command.RecipientEmails,
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

    private static async Task<Result<FileObject>> LoadShareableFile(
        CreateShareLinkCommand command,
        IFileObjectRepository files,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null || !command.Scope.CanAccess(file))
            return Result.Failure<FileObject>(FileErrors.NotFound);
        if (file.Status != FileStatus.Available)
            return Result.Failure<FileObject>(FileErrors.NotAvailable);
        return Result.Success(file);
    }
}

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

/// <summary>Logica de creacion compartida por File y Folder — evita duplicar validacion/persistencia/evento entre los dos handlers.</summary>
internal static class ShareLinkCreationCore
{
    public static async Task<Result<CreatedShareLinkResponse>> CreateAndPersist(
        Guid tenantId,
        Guid actorId,
        bool actorHasManagePermission,
        Guid resourceId,
        ShareResourceType resourceType,
        ShareVisibility visibility,
        SharePermission permission,
        string? password,
        DateTime? expiresAtUtc,
        int? maxAccessCount,
        bool isRecursive,
        bool appliesToFutureItems,
        IReadOnlyList<Guid> recipientUserIds,
        IReadOnlyList<Guid> recipientCustomerIds,
        IReadOnlyList<string> recipientEmails,
        RequestAuditContext auditContext,
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
        var policyResult = await ValidatePolicy(
            actorHasManagePermission,
            permission,
            visibility,
            tenantId,
            recipientUserIds,
            recipientCustomerIds,
            recipientEmails,
            limits,
            ct
        );
        if (policyResult.IsFailure)
            return Result.Failure<CreatedShareLinkResponse>(policyResult.Error);

        var created = ShareLink.Create(
            Guid.NewGuid(),
            tenantId,
            resourceId,
            resourceType,
            visibility,
            permission,
            string.IsNullOrWhiteSpace(password) ? null : passwordHasher.Hash(password),
            expiresAtUtc,
            maxAccessCount,
            actorId,
            clock.UtcNow,
            isRecursive,
            appliesToFutureItems
        );
        if (created.IsFailure)
            return Result.Failure<CreatedShareLinkResponse>(created.Error);

        var (link, plainToken) = created.Value;
        foreach (var userId in recipientUserIds)
            link.AddUserRecipient(userId);
        foreach (var customerId in recipientCustomerIds)
            link.AddCustomerRecipient(customerId);
        foreach (var email in recipientEmails)
            link.AddExternalRecipient(email);

        shares.Add(link);
        audit.Add(
            StorageAccessLog.Create(
                tenantId,
                resourceId,
                actorId,
                "share.create",
                "success",
                auditContext.IpAddress,
                auditContext.UserAgent,
                auditContext.CorrelationId,
                $"resourceType={resourceType};visibility={visibility};permission={permission}",
                clock.UtcNow
            )
        );
        await bus.PublishAsync(
            new ShareLinkCreatedIntegrationEvent
            {
                TenantId = tenantId,
                ShareLinkId = link.Id,
                ResourceId = resourceId,
                ResourceType = resourceType.ToString(),
                Visibility = visibility.ToString(),
                CreatedByUserId = actorId,
                CorrelationId = auditContext.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new CreatedShareLinkResponse(ShareLinkResponseMapper.Map(link, clock.UtcNow), plainToken)
        );
    }

    internal static async Task<Result> ValidatePolicy(
        bool actorHasManagePermission,
        SharePermission permission,
        ShareVisibility visibility,
        Guid tenantId,
        IReadOnlyList<Guid> recipientUserIds,
        IReadOnlyList<Guid> recipientCustomerIds,
        IReadOnlyList<string> recipientEmails,
        IStorageLimitRepository limits,
        CancellationToken ct
    )
    {
        if (permission is SharePermission.Upload or SharePermission.EditMetadata && !actorHasManagePermission)
            return Result.Failure(ShareErrors.ElevatedPermissionRequiresManage);

        // Fase C4 (completitud) — §20.4 del plan: "en un PublicLink, nunca Upload/
        // Edit/ShareAgain". Antes solo se exigia cloudstorage.share.manage para
        // otorgar Upload/EditMetadata, pero nada impedia que ese mismo actor los
        // combinara con Visibility.Public (un link sin autenticacion con permiso
        // de escritura).
        if (
            visibility == ShareVisibility.Public
            && permission is SharePermission.Upload or SharePermission.EditMetadata
        )
            return Result.Failure(ShareErrors.ElevatedPermissionNotAllowedOnPublicLink);

        if (visibility == ShareVisibility.Public)
        {
            var limit = await limits.GetAsync(tenantId, ct);
            if (limit is null || !limit.AllowPublicShareLinks)
                return Result.Failure(ShareErrors.PublicSharingDisabled);
        }

        var needsRecipients = visibility is ShareVisibility.SpecificUsers or ShareVisibility.ExternalRecipients;
        var hasRecipients = recipientUserIds.Count > 0 || recipientCustomerIds.Count > 0 || recipientEmails.Count > 0;
        if (needsRecipients && !hasRecipients)
            return Result.Failure(ShareErrors.RecipientsRequired);

        return Result.Success();
    }
}

/// <summary>Fase C3 — revoca un link de compartir. Irreversible: no existe "des-revocar".</summary>
public sealed record RevokeShareLinkCommand(Guid TenantId, Guid ActorId, Guid ShareLinkId, RequestAuditContext Audit);

public static class RevokeShareLinkHandler
{
    public static async Task<Result> Handle(
        RevokeShareLinkCommand command,
        IShareLinkRepository shares,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var link = await shares.GetAsync(command.TenantId, command.ShareLinkId, ct);
        if (link is null)
            return Result.Failure(ShareErrors.NotFound);

        var revoked = link.Revoke(clock.UtcNow);
        if (revoked.IsFailure)
            return Result.Failure(revoked.Error);

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                link.ResourceId,
                command.ActorId,
                "share.revoke",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                null,
                clock.UtcNow
            )
        );
        await bus.PublishAsync(
            new ShareLinkRevokedIntegrationEvent
            {
                TenantId = command.TenantId,
                ShareLinkId = link.Id,
                ResourceId = link.ResourceId,
                ResourceType = link.ResourceType.ToString(),
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>Fase C3 — extiende o acorta la expiracion de un link activo.</summary>
public sealed record UpdateShareExpirationCommand(Guid TenantId, Guid ShareLinkId, DateTime NewExpiresAtUtc);

public static class UpdateShareExpirationHandler
{
    public static async Task<Result<ShareLinkResponse>> Handle(
        UpdateShareExpirationCommand command,
        IShareLinkRepository shares,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var link = await shares.GetAsync(command.TenantId, command.ShareLinkId, ct);
        if (link is null)
            return Result.Failure<ShareLinkResponse>(ShareErrors.NotFound);

        var updated = link.UpdateExpiration(command.NewExpiresAtUtc, clock.UtcNow);
        if (updated.IsFailure)
            return Result.Failure<ShareLinkResponse>(updated.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ShareLinkResponseMapper.Map(link, clock.UtcNow));
    }
}

/// <summary>
/// Fase C4 (completitud) — cambia el Permission de un link ya creado. Reusa
/// exactamente la misma validacion de politica que la creacion (§20.4 del plan):
/// Upload/EditMetadata solo con cloudstorage.share.manage, y nunca junto con
/// Visibility.Public.
/// </summary>
public sealed record ChangeSharePermissionCommand(
    Guid TenantId,
    Guid ActorId,
    bool ActorHasManagePermission,
    Guid ShareLinkId,
    SharePermission NewPermission,
    RequestAuditContext Audit
);

public static class ChangeSharePermissionHandler
{
    public static async Task<Result<ShareLinkResponse>> Handle(
        ChangeSharePermissionCommand command,
        IShareLinkRepository shares,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var link = await shares.GetAsync(command.TenantId, command.ShareLinkId, ct);
        if (link is null)
            return Result.Failure<ShareLinkResponse>(ShareErrors.NotFound);

        var policyResult = await ShareLinkCreationCore.ValidatePolicy(
            command.ActorHasManagePermission,
            command.NewPermission,
            link.Visibility,
            command.TenantId,
            recipientUserIds: [],
            recipientCustomerIds: [],
            recipientEmails: [],
            limits,
            ct
        );
        if (policyResult.IsFailure)
            return Result.Failure<ShareLinkResponse>(policyResult.Error);

        var oldPermission = link.ChangePermission(command.NewPermission);
        if (oldPermission == command.NewPermission)
        {
            // No-op: no hace falta auditar ni publicar un evento por un cambio que no cambia nada.
            return Result.Success(ShareLinkResponseMapper.Map(link, clock.UtcNow));
        }

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                link.ResourceId,
                command.ActorId,
                "share.permission-changed",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                $"oldPermission={oldPermission};newPermission={command.NewPermission}",
                clock.UtcNow
            )
        );
        await bus.PublishAsync(
            new ShareLinkPermissionChangedIntegrationEvent
            {
                TenantId = command.TenantId,
                ShareLinkId = link.Id,
                ResourceId = link.ResourceId,
                OldPermission = oldPermission.ToString(),
                NewPermission = command.NewPermission.ToString(),
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ShareLinkResponseMapper.Map(link, clock.UtcNow));
    }
}
