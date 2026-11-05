using BuildingBlocks.Persistence;

namespace TaxVision.Postmaster.Application.Sending.Commands.ReleaseSendAttempt;

public static class ReleaseSendAttemptHandler
{
    public static async Task Handle(
        ReleaseSendAttemptCommand command,
        ISentMessageRepository sentMessages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await sentMessages.GetByIdForFeedbackAsync(command.SentMessageId, ct);
        if (found.IsFailure || found.Value.ReleaseSendAttempt().IsFailure)
            return;

        await unitOfWork.SaveChangesAsync(ct);
    }
}
