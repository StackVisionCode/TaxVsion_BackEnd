using BuildingBlocks.Persistence;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.Commands.DiscardUnsentMessage;

public static class DiscardUnsentMessageHandler
{
    public static async Task Handle(
        DiscardUnsentMessageCommand command,
        ISentMessageRepository sentMessages,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await sentMessages.GetByIdForFeedbackAsync(command.SentMessageId, ct);
        // Con el intento grabado ya no se sabe si salió: no se borra.
        if (found.IsFailure || found.Value.SendAttemptedAtUtc is not null)
            return;

        var message = found.Value;
        sentMessages.Remove(message);
        await idempotencyGuard.ReleaseAsync(message.TenantId, message.IdempotencyKey, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
