using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Suppression;
using Wolverine;

namespace TaxVision.Postmaster.Application.Feedback.Commands.ApplyPolledDeliveryFeedback;

/// <summary>Transaccional y sin proveedor: aplica un lote ya consultado, con su outbox.</summary>
public static class ApplyPolledDeliveryFeedbackHandler
{
    public static Task<DeliveryFeedbackTally> Handle(
        ApplyPolledDeliveryFeedbackCommand cmd,
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
        var stores = new DeliveryFeedbackStores(sentMessages, receipts, unmatched, suppressionList, unitOfWork, bus);
        return DeliveryFeedbackApplier.ApplyAllAsync(cmd.Items, cmd.ReceivedAtUtc, stores, logger, ct);
    }
}
