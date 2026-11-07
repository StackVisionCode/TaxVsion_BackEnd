using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.CloudStorage.Domain.Files.Events;

namespace TaxVision.CloudStorage.Application.Files.DomainEvents;

public static class FileScanFailedDomainEventHandler
{
    public static FileScanFailedIntegrationEvent Handle(FileScanFailedDomainEvent failed) =>
        new()
        {
            TenantId = failed.TenantId,
            FileId = failed.FileId,
            ObjectKey = failed.ObjectKey,
            Reason = failed.Reason,
            OwnerType = failed.OwnerType,
            OwnerId = failed.OwnerId,
            FolderType = failed.FolderType,
            CreatedBy = failed.CreatedBy,
            CorrelationId = failed.CorrelationId,
        };
}
