using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Postmaster.Application.Feedback.Commands.ProcessDeliveryFeedback;

/// <summary>
/// Modo push: traduce el payload con el adaptador de su origen y aplica cada aviso. Un aviso sin dueño
/// se guarda aparte para diagnóstico, nunca rompe el resto del lote.
/// </summary>
public static class ProcessDeliveryFeedbackHandler
{
    public static async Task<Result<DeliveryFeedbackTally>> Handle(
        ProcessDeliveryFeedbackCommand cmd,
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
        var translated = TranslateWithSourceAdapter(cmd, capabilities);
        if (translated.IsFailure)
            return Result.Failure<DeliveryFeedbackTally>(translated.Error);

        var stores = new DeliveryFeedbackStores(sentMessages, receipts, unmatched, suppressionList, unitOfWork, bus);
        return Result.Success(
            await DeliveryFeedbackApplier.ApplyAllAsync(
                translated.Value,
                cmd.ReceivedAtUtc,
                stores,
                logger,
                ct,
                cmd.ExpectedTenantId
            )
        );
    }

    private static Result<IReadOnlyList<TranslatedFeedback>> TranslateWithSourceAdapter(
        ProcessDeliveryFeedbackCommand cmd,
        IDeliveryFeedbackCapabilities capabilities
    )
    {
        var source = FeedbackSource.Create(cmd.Source);
        if (source.IsFailure)
            return Result.Failure<IReadOnlyList<TranslatedFeedback>>(source.Error);

        var translator = capabilities.FindTranslator(source.Value);
        return translator is null
            ? Result.Failure<IReadOnlyList<TranslatedFeedback>>(
                new Error("DeliveryFeedback.UnknownSource", $"No translator registered for '{cmd.Source}'.")
            )
            : translator.Translate(cmd.RawPayload);
    }
}
