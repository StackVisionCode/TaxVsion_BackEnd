using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// El listener vio correo nuevo: se corre la reconciliación de siempre. Dos avisos seguidos no
/// duplican nada: el lock de la cuenta y la marca "pendiente" los juntan en una pasada más.
/// </summary>
public static class ConnectorsMailboxChangeDetectedConsumer
{
    public static async Task Handle(
        ConnectorsMailboxChangeDetectedIntegrationEvent evt,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var result = await bus.InvokeAsync<Result<ReconciliationOutcome>>(
                new ReconcileAccountCommand(evt.AccountId),
                ct
            );
            if (result.IsFailure)
                logger.LogWarning(
                    "Sync after mailbox change on account {AccountId} failed: {Code}",
                    evt.AccountId,
                    result.Error.Code
                );
        }
    }
}
