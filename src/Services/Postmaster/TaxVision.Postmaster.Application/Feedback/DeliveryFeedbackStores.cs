using BuildingBlocks.Persistence;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Suppression;
using Wolverine;

namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>Lo que necesita <see cref="DeliveryFeedbackApplier"/> para guardar y avisar; lo arma cada handler.</summary>
internal sealed record DeliveryFeedbackStores(
    ISentMessageRepository SentMessages,
    IDeliveryFeedbackReceiptRepository Receipts,
    IUnmatchedDeliveryFeedbackRepository Unmatched,
    ISuppressionListRepository SuppressionList,
    IUnitOfWork UnitOfWork,
    IMessageBus Bus
);
