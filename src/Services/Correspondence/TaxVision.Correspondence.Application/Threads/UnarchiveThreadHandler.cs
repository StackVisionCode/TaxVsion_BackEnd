using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using Wolverine;

namespace TaxVision.Correspondence.Application.Threads;

// Archived → Active. Idempotente (Unarchive no-op si ya está activo).
public static class UnarchiveThreadHandler
{
    public static async Task<Result> Handle(
        UnarchiveThreadCommand command,
        IEmailThreadRepository emailThreads,
        IIncomingEmailRepository incomingEmails,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var thread = await emailThreads.GetByIdAsync(command.TenantId, command.ThreadId, ct);
        if (
            thread is null
            || !await ThreadAccess.CanSeeAsync(
                emailThreads,
                thread,
                command.VisibleAccountIds,
                command.RestrictToUserId,
                ct
            )
        )
            return Result.Failure(ThreadAccess.NotFound);

        thread.Unarchive();
        await unitOfWork.SaveChangesAsync(ct);
        await ThreadStateNotifier.PublishAsync(
            bus,
            incomingEmails,
            thread,
            command.ActorUserId,
            command.CorrelationId,
            ct
        );
        return Result.Success();
    }
}
