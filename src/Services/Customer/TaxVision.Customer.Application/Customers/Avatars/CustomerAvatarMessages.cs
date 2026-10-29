using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Avatars;

/// <summary>
/// Mensajes hacia CloudStorage para la foto del cliente. Dueño <c>Customer/{customerId}</c> en la carpeta
/// <c>Avatars</c>: el borrado se verifica contra ese mismo dueño, así nunca toca un archivo ajeno.
/// </summary>
public static class CustomerAvatarMessages
{
    public const string OwnerType = "Customer";
    public const string FolderType = "Avatars";
    public const string RequestingService = "customer";
    public const string DeletionSource = "customer.avatars";
    public const string OriginalName = "avatar.webp";

    public static class Reasons
    {
        public const string Replaced = "avatar_replaced";
        public const string Removed = "avatar_removed";
        public const string Orphaned = "avatar_orphaned";
        public const string CustomerArchived = "customer_archived";
    }

    public static bool IsCustomerAvatar(string? ownerType, string? folderType) =>
        string.Equals(ownerType, OwnerType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(folderType, FolderType, StringComparison.OrdinalIgnoreCase);

    public static SaveFileRequestedIntegrationEvent SaveRequested(
        Guid tenantId,
        Guid customerId,
        Guid fileId,
        CustomerAvatarSourceObject source,
        string contentType,
        long sizeBytes,
        Guid actorUserId,
        string correlationId
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            FileId = fileId,
            RequestingService = RequestingService,
            SourceBucket = source.Bucket,
            SourceObjectKey = source.ObjectKey,
            ActorId = actorUserId,
            OwnerType = OwnerType,
            OwnerId = customerId,
            FolderType = FolderType,
            TaxYear = null,
            OriginalName = OriginalName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
        };

    public static FilesDeletionRequestedIntegrationEvent Deletion(
        Guid tenantId,
        Guid customerId,
        Guid fileId,
        string reason,
        string correlationId
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            SourceService = DeletionSource,
            SourceReferenceId = customerId,
            Reason = reason,
            Files =
            [
                new FileDeletionTarget
                {
                    FileId = fileId,
                    ExpectedOwnerType = OwnerType,
                    ExpectedOwnerId = customerId,
                    ExpectedFolderType = FolderType,
                },
            ],
        };
}
