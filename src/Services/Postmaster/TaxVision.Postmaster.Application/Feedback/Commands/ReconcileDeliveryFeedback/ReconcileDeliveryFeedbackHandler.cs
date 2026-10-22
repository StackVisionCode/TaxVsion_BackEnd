using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Postmaster.Application.Feedback.Commands.ReconcileDeliveryFeedback;

/// <summary>
/// Hoy el único proveedor del sistema es SMTP2GO. Sin poller registrado (no hay API key) no hace nada:
/// el webhook sigue funcionando solo.
/// </summary>
public static class ReconcileDeliveryFeedbackHandler
{
    public static async Task<DeliveryFeedbackTally> Handle(
        ReconcileDeliveryFeedbackCommand cmd,
        IDeliveryFeedbackCapabilities capabilities,
        ISentMessageRepository sentMessages,
        IDeliveryFeedbackReceiptRepository receipts,
        IUnmatchedDeliveryFeedbackRepository unmatched,
        ISuppressionListRepository suppressionList,
        IUnitOfWork unitOfWork,
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
        var stores = new DeliveryFeedbackStores(sentMessages, receipts, unmatched, suppressionList, unitOfWork, bus);
        var tally = await DeliveryFeedbackApplier.ApplyAllAsync(polled, cmd.NowUtc, stores, logger, ct);

        logger.LogInformation(
            "Delivery feedback reconciliation checked {Pending} recipient(s): {Applied} applied, {Duplicates} already known.",
            pending.Count,
            tally.Applied,
            tally.Duplicates
        );
        return tally;
    }
}
