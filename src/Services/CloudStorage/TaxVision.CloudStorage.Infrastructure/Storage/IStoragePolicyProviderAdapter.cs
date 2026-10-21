using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>Vendor adapter. CRM policy selection and scheduling stay outside provider-specific API clients.</summary>
public interface IStoragePolicyProviderAdapter
{
    string ProviderId { get; }
    Task CopyAvailableFileAsync(FileAvailableIntegrationEvent message, CancellationToken ct);
    Task BackupTenantAsync(Guid tenantId, CancellationToken ct);
}
