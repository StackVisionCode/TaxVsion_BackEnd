using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Api.Consumers;

/// <summary>La copia externa se ejecuta después de que CloudStorage marca limpio el archivo.</summary>
public static class CloudStorageFileMirrorConsumer
{
    public static Task Handle(
        FileAvailableIntegrationEvent message,
        StoragePolicyDispatcher dispatcher,
        CancellationToken ct
    ) => dispatcher.CopyAvailableFileAsync(message, ct);
}
