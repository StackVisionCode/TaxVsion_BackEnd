using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TaxVision.Customer.Application.Imports.Messages;

/// <summary>
/// Worker que procesa el archivo de import en chunks. Corre en background via Wolverine.
///
/// Diseno:
///  - Cada chunk se procesa en su propio scope DI (transaccion-per-chunk), ver <see cref="ImportChunkProcessor"/>.
///  - El attempt aggregate vive en un scope separado para que su progreso commitee
///    independientemente de los chunks de customers, ver <see cref="ImportAttemptLifecycle"/>.
///  - Cancelacion cooperativa: antes de cada chunk se recarga el estado del attempt;
///    si esta en Canceling, se confirma cancelacion y se sale.
///  - 1 solo evento CustomersBulkImportedV1 al final (no por fila).
/// </summary>
public static class RunCustomerImportHandler
{
    public static async Task Handle(
        RunCustomerImportMessage msg,
        IServiceScopeFactory scopeFactory,
        ILogger<RunCustomerImportMessage> logger,
        CancellationToken ct
    )
    {
        var attemptId = msg.ImportAttemptId;
        logger.LogInformation("Worker received import {AttemptId}", attemptId);

        try
        {
            var startInfo = await ImportAttemptLifecycle.StartAsync(scopeFactory, attemptId, logger, ct);
            if (startInfo is null)
                return;

            var allRows = await ImportAttemptLifecycle.ReadRowsAsync(
                scopeFactory,
                startInfo.TenantId,
                attemptId,
                startInfo.SourceKind,
                ct
            );
            var maxRows = ImportAttemptLifecycle.ResolveMaxRows(scopeFactory);
            if (
                !await ImportAttemptLifecycle.ValidateRowCountAsync(scopeFactory, attemptId, allRows.Count, maxRows, ct)
            )
                return;

            if (!await ImportAttemptLifecycle.MoveToApplyingAsync(scopeFactory, attemptId, allRows.Count, logger, ct))
                return;

            var aggregate = await ImportChunkProcessor.ProcessAllAsync(
                scopeFactory,
                attemptId,
                startInfo,
                allRows,
                logger,
                ct
            );

            await ImportAttemptLifecycle.FinishAsync(
                scopeFactory,
                attemptId,
                startInfo,
                aggregate,
                allRows.Count,
                logger,
                ct
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Import {AttemptId} interrupted by host shutdown", attemptId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import {AttemptId} crashed", attemptId);
            await ImportAttemptLifecycle.FailAsync(scopeFactory, attemptId, ex.Message, CancellationToken.None);
        }
    }
}
