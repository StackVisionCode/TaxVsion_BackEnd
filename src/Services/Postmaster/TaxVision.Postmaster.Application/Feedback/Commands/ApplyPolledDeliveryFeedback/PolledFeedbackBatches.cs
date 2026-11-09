using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace TaxVision.Postmaster.Application.Feedback.Commands.ApplyPolledDeliveryFeedback;

/// <summary>
/// Aplica lo consultado por lotes, cada uno en su transacción. Un choque con el webhook deja ese lote para la próxima
/// vuelta (lo aplicado se descarta por recibo) y no corta los demás.
/// </summary>
internal static class PolledFeedbackBatches
{
    public static async Task<DeliveryFeedbackTally> ApplyAsync(
        IReadOnlyList<TranslatedFeedback> items,
        DateTime receivedAtUtc,
        int batchSize,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        var tally = DeliveryFeedbackTally.Empty;
        foreach (var batch in items.Chunk(Math.Max(1, batchSize)))
        {
            try
            {
                tally = tally.Plus(
                    await bus.InvokeAsync<DeliveryFeedbackTally>(
                        new ApplyPolledDeliveryFeedbackCommand(batch, receivedAtUtc),
                        ct
                    )
                );
            }
            catch (ConflictException ex)
            {
                logger.LogInformation(
                    "Polled delivery feedback batch of {Count} left for the next run: {Code}.",
                    batch.Length,
                    ex.Code
                );
            }
        }
        return tally;
    }
}
