using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Feedback.Commands.ApplyPolledDeliveryFeedback;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Postmaster.Application.Feedback.Commands.ReconcileEngagementFeedback;

/// <summary>
/// Trae entregas, aperturas y clics por rango y aplica solo los de correos nuestros. La cuenta de SMTP2GO puede ser
/// compartida con otros entornos: sus eventos se descartan antes de aplicar y no van a "unmatched". La consulta
/// corre sin transacción (PMS-06); aplicar es otro comando.
/// </summary>
public static class ReconcileEngagementFeedbackHandler
{
    [NonTransactional]
    public static async Task<DeliveryFeedbackTally> Handle(
        ReconcileEngagementFeedbackCommand cmd,
        IDeliveryFeedbackCapabilities capabilities,
        ISentMessageRepository sentMessages,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        var poller = capabilities.FindEngagementPoller(FeedbackSource.Smtp2Go);
        if (poller is null)
            return DeliveryFeedbackTally.Empty;

        var polled = await poller.PollEngagementAsync(cmd.FromUtc, cmd.ToUtc, cmd.MaxEvents, ct);
        if (polled.Count == 0)
            return DeliveryFeedbackTally.Empty;

        var ours = await KeepOnlyOurMessagesAsync(polled, sentMessages, ct);
        var tally = await PolledFeedbackBatches.ApplyAsync(ours, cmd.NowUtc, cmd.ApplyBatchSize, bus, logger, ct);

        logger.LogInformation(
            "Recent activity reconciliation (delivered/opened/clicked) read {Read} event(s), {Foreign} from other senders: {Applied} applied, {Duplicates} already known.",
            polled.Count,
            polled.Count - ours.Count,
            tally.Applied,
            tally.Duplicates
        );
        return tally;
    }

    private static async Task<IReadOnlyList<TranslatedFeedback>> KeepOnlyOurMessagesAsync(
        IReadOnlyList<TranslatedFeedback> polled,
        ISentMessageRepository sentMessages,
        CancellationToken ct
    )
    {
        var providerIds = polled.Select(p => p.ProviderMessageId).OfType<string>().ToList();
        var known = await sentMessages.FindKnownProviderMessageIdsAsync(providerIds, ct);
        return polled.Where(p => p.ProviderMessageId is { } id && known.Contains(id)).ToList();
    }
}
