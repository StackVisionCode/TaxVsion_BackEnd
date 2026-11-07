using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.CloudStorage.Domain.Files.Events;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.DomainEvents;

/// <summary>Cada motivo de cuarentena tiene su evento de integración: los contratos no cambian.</summary>
public static class FileQuarantinedDomainEventHandler
{
    public static OutgoingMessages Handle(FileQuarantinedDomainEvent quarantined) =>
        quarantined.Kind switch
        {
            FileQuarantineKind.Infected =>
            [
                new FileInfectedDetectedIntegrationEvent
                {
                    TenantId = quarantined.TenantId,
                    FileId = quarantined.FileId,
                    ObjectKey = quarantined.ObjectKey,
                    ScanReport = quarantined.Reason,
                    OwnerType = quarantined.OwnerType,
                    OwnerId = quarantined.OwnerId,
                    FolderType = quarantined.FolderType,
                    CorrelationId = quarantined.CorrelationId,
                },
            ],
            FileQuarantineKind.BlockedByPolicy =>
            [
                new FileBlockedByPolicyIntegrationEvent
                {
                    TenantId = quarantined.TenantId,
                    FileId = quarantined.FileId,
                    ObjectKey = quarantined.ObjectKey,
                    PolicyReason = quarantined.Reason,
                    CreatedBy = quarantined.CreatedBy,
                    OwnerType = quarantined.OwnerType,
                    OwnerId = quarantined.OwnerId,
                    FolderType = quarantined.FolderType,
                    CorrelationId = quarantined.CorrelationId,
                },
            ],
            _ =>
            [
                new FilePendingReviewIntegrationEvent
                {
                    TenantId = quarantined.TenantId,
                    FileId = quarantined.FileId,
                    ObjectKey = quarantined.ObjectKey,
                    Reason = quarantined.Reason,
                    CreatedBy = quarantined.CreatedBy,
                    OwnerType = quarantined.OwnerType,
                    OwnerId = quarantined.OwnerId,
                    FolderType = quarantined.FolderType,
                    CorrelationId = quarantined.CorrelationId,
                },
            ],
        };
}
