using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Application.Files.Consumers;

/// <summary>La copia externa se ejecuta después de que CloudStorage marca limpio el archivo.</summary>
public static class CloudStorageFileMirrorConsumer
{
    public static Task Handle(
        FileAvailableIntegrationEvent message,
        IStoragePolicyDispatcher dispatcher,
        CancellationToken ct
    ) => dispatcher.CopyAvailableFileAsync(message, ct);
}
