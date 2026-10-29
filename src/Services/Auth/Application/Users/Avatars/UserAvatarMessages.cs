using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Users.Avatars;

/// <summary>
/// Mensajes del ciclo de vida de la foto de perfil del staff. Dueño en CloudStorage: User/userId,
/// carpeta Avatars (la única que sirve <c>/storage/avatars/{fileId}</c> sin token).
/// </summary>
public static class UserAvatarMessages
{
    public const string OwnerType = "User";
    public const string FolderType = "Avatars";
    public const string FileName = "avatar.webp";
    public const string SourceService = "auth.avatars";

    public static SaveFileRequestedIntegrationEvent SaveFileRequested(
        User user,
        StoredUserAvatar stored,
        string contentType,
        long sizeBytes,
        string correlationId
    ) =>
        new()
        {
            TenantId = user.TenantId,
            FileId = stored.FileId,
            RequestingService = "auth",
            SourceBucket = stored.SourceBucket,
            SourceObjectKey = stored.SourceObjectKey,
            ActorId = user.Id,
            OwnerType = OwnerType,
            OwnerId = user.Id,
            FolderType = FolderType,
            TaxYear = null,
            OriginalName = FileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            CorrelationId = correlationId,
        };

    /// <summary>Borrado por evento: CloudStorage verifica dueño y carpeta antes de mandarlo a la papelera.</summary>
    public static FilesDeletionRequestedIntegrationEvent? FilesDeletionRequested(
        User user,
        IEnumerable<Guid> fileIds,
        string reason,
        string correlationId
    )
    {
        var targets = fileIds
            .Distinct()
            .Select(fileId => new FileDeletionTarget
            {
                FileId = fileId,
                ExpectedOwnerType = OwnerType,
                ExpectedOwnerId = user.Id,
                ExpectedFolderType = FolderType,
            })
            .ToList();

        return targets.Count == 0
            ? null
            : new FilesDeletionRequestedIntegrationEvent
            {
                TenantId = user.TenantId,
                SourceService = SourceService,
                SourceReferenceId = user.Id,
                Reason = reason,
                Files = targets,
                CorrelationId = correlationId,
            };
    }

    /// <summary>Estado actual completo del perfil visible (nombre + foto).</summary>
    public static UserProfileUpdatedIntegrationEvent ProfileUpdated(User user, string correlationId) =>
        new()
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            Name = user.Name,
            LastName = user.LastName,
            AvatarFileId = user.AvatarFileId,
            CorrelationId = correlationId,
        };

    /// <summary>¿El archivo es una foto de perfil de staff? Lo deciden los metadatos del evento de CloudStorage.</summary>
    public static bool IsUserAvatar(string? ownerType, string? folderType, Guid? ownerId) =>
        ownerId is { } id
        && id != Guid.Empty
        && string.Equals(ownerType, OwnerType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(folderType, FolderType, StringComparison.OrdinalIgnoreCase);
}
