using TaxVision.CloudStorage.Application.Files.Commands;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Messaging;

/// <summary>Cola local del escaneo; la usan Program.cs y la prueba de durabilidad.</summary>
public static class ScanQueueConfiguration
{
    public static WolverineOptions ConfigureScanQueue(this WolverineOptions options, int scanParallelism)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scanParallelism, 1);

        // Paralelismo acotado (CST-16): el choque en la cuota lo resuelve la fusión, con el tope de CST-4.
        // Un mismo archivo no se escanea dos veces por IScanClaims, y el RowVersion de Files respalda.
        // Durable: UseDurableOutboxOnAllSendingEndpoints no cubre las colas locales (CST-1).
        options.LocalQueueFor<ScanFileCommand>().UseDurableInbox().MaximumParallelMessages(scanParallelism);
        // Mover/borrar el objeto escaneado sale después del veredicto y no se puede perder (CST-3).
        options.LocalQueueFor<RelocateScannedBlobCommand>().UseDurableInbox();
        return options;
    }
}
