using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Feedback.Commands.ApplyPolledDeliveryFeedback;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Postmaster.Application.Feedback.Commands.ReconcileDeliveryFeedback;

/// <summary>
/// Hoy el único proveedor del sistema es SMTP2GO. Sin poller registrado (no hay API key) no hace nada:
/// el webhook sigue funcionando solo. La consulta corre sin transacción (PMS-06); aplicar es otro comando.
/// </summary>
public static class ReconcileDeliveryFeedbackHandler
{
    [NonTransactional]
    public static async Task<DeliveryFeedbackTally> Handle(
        ReconcileDeliveryFeedbackCommand cmd,
        IDeliveryFeedbackCapabilities capabilities,
        ISentMessageRepository sentMessages,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        var poller = capabilities.FindPoller(FeedbackSource.Smtp2Go);
        if (poller is null)
            return DeliveryFeedbackTally.Empty;

        // Uno más que el tope: si vuelven más, hubo pendientes que quedan para la próxima corrida.
        var pending = await sentMessages.ListPendingDeliveryAsync(
            cmd.SentFromUtc,
            cmd.SentToUtc,
            cmd.MaxRecipients + 1,
            ct
        );
        if (pending.Count == 0)
            return DeliveryFeedbackTally.Empty;
        if (pending.Count > cmd.MaxRecipients)
        {
            logger.LogWarning(
                "Delivery feedback reconciliation capped at {Max} recipients; the rest wait for the next run.",
                cmd.MaxRecipients
            );
            pending = pending.Take(cmd.MaxRecipients).ToList();
        }

        var polled = await poller.PollAsync(pending, ct);
        var tally = await PolledFeedbackBatches.ApplyAsync(polled, cmd.NowUtc, cmd.ApplyBatchSize, bus, logger, ct);

        logger.LogInformation(
            "Delivery feedback reconciliation checked {Pending} recipient(s): {Applied} applied, {Duplicates} already known.",
            pending.Count,
            tally.Applied,
            tally.Duplicates
        );
        return tally;
    }
}
