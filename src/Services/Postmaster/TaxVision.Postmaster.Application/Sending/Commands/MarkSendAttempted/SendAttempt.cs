using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;
using Wolverine;

namespace TaxVision.Postmaster.Application.Sending.Commands.MarkSendAttempted;

public static class SendAttempt
{
    /// <summary>Un choque de versión dice que otra entrega marcó el intento primero: esa envía, esta no.</summary>
    public static async Task<Result> TryMarkAsync(IMessageBus bus, Guid sentMessageId, CancellationToken ct)
    {
        try
        {
            return await bus.InvokeAsync<Result>(new MarkSendAttemptedCommand(sentMessageId), ct);
        }
        catch (ConflictException ex) when (ex.Code == RecordSendResultRetry.ConcurrencyConflictCode)
        {
            return Result.Failure(new Error(ex.Code, ex.Message));
        }
    }
}
