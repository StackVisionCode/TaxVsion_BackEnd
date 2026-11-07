using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

namespace TaxVision.CloudStorage.Application.Abstractions;

/// <summary>Aplica la política de almacenamiento del tenant a un archivo ya marcado limpio.</summary>
public interface IStoragePolicyDispatcher
{
    Task CopyAvailableFileAsync(FileAvailableIntegrationEvent message, CancellationToken ct);
}
