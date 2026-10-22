using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>Vendor adapter. CRM policy selection and scheduling stay outside provider-specific API clients.</summary>
public interface IStoragePolicyProviderAdapter
{
    string ProviderId { get; }
    Task CopyAvailableFileAsync(FileAvailableIntegrationEvent message, CancellationToken ct);
    Task BackupTenantAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>
/// Lo que un mirror necesita para copiar un archivo. El respaldo completo no reconstruye un
/// FileAvailableIntegrationEvent: ese evento lo publica el escaneo, no se fabrica para uso interno.
/// </summary>
internal sealed record FileToMirror(Guid TenantId, Guid FileId, string ChecksumSha256)
{
    public static FileToMirror From(FileAvailableIntegrationEvent message) =>
        new(message.TenantId, message.FileId, message.ChecksumSha256);
}
