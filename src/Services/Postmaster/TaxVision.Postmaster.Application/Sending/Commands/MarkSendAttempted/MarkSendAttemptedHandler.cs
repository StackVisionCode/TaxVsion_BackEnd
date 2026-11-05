using BuildingBlocks.Persistence;
using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Application.Sending.Commands.MarkSendAttempted;

/// <summary>Transacción corta propia. Falla si el mensaje ya no está enviándose o si otro intento ya lo tomó.</summary>
public static class MarkSendAttemptedHandler
{
    public static async Task<Result> Handle(
        MarkSendAttemptedCommand command,
        ISentMessageRepository sentMessages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await sentMessages.GetByIdForFeedbackAsync(command.SentMessageId, ct);
        if (found.IsFailure)
            return Result.Failure(found.Error);

        var marked = found.Value.MarkSendAttempted(DateTime.UtcNow);
        if (marked.IsFailure)
            return marked;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
