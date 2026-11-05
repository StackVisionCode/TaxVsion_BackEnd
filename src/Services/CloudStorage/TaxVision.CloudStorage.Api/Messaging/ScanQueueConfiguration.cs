using TaxVision.CloudStorage.Application.Files.Commands;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Messaging;

/// <summary>Cola local del escaneo; la usan Program.cs y la prueba de durabilidad.</summary>
public static class ScanQueueConfiguration
{
    public static WolverineOptions ConfigureScanQueue(this WolverineOptions options)
    {
        // Cada escaneo actualiza la proyección de cuota del tenant. Se procesan en serie para
        // evitar que dos archivos compitan por el mismo RowVersion después de mover el objeto.
        // Durable: UseDurableOutboxOnAllSendingEndpoints no cubre las colas locales (CST-1).
        options.LocalQueueFor<ScanFileCommand>().UseDurableInbox().Sequential();
        // Mover/borrar el objeto escaneado sale después del veredicto y no se puede perder (CST-3).
        options.LocalQueueFor<RelocateScannedBlobCommand>().UseDurableInbox();
        return options;
    }
}
