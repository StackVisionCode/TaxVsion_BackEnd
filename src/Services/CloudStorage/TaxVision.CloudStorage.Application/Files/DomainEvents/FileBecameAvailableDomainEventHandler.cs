using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.CloudStorage.Domain.Files.Events;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.DomainEvents;

/// <summary><c>FileAvailable</c> y, si el cliente lo subió desde el portal, el aviso de documento nuevo. Todo sale del evento.</summary>
public static class FileBecameAvailableDomainEventHandler
{
    public static OutgoingMessages Handle(FileBecameAvailableDomainEvent available)
    {
        var messages = new OutgoingMessages
        {
            new FileAvailableIntegrationEvent
            {
                TenantId = available.TenantId,
                FileId = available.FileId,
                ObjectKey = available.ObjectKey,
                ContentType = available.ContentType,
                SizeBytes = available.SizeBytes,
                ChecksumSha256 = available.ChecksumSha256,
                CreatedBy = available.CreatedBy,
                OwnerType = available.OwnerType,
                OwnerId = available.OwnerId,
                FolderId = available.FolderId,
                FolderType = available.FolderType,
                OriginalName = available.OriginalName,
                TaxYear = available.TaxYear,
                CorrelationId = available.CorrelationId,
            },
        };

        if (available.PortalCustomerId is { } customerId)
            messages.Add(
                new CustomerDocumentUploadedIntegrationEvent
                {
                    TenantId = available.TenantId,
                    CustomerId = customerId,
                    FileId = available.FileId,
                    FolderId = available.FolderId,
                    CorrelationId = available.CorrelationId,
                }
            );

        return messages;
    }
}
