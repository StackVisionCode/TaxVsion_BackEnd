using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Threads;

// Archived → Active. Idempotente (Unarchive no-op si ya está activo).
public static class UnarchiveThreadHandler
{
    public static async Task<Result> Handle(
        UnarchiveThreadCommand command,
        IEmailThreadRepository emailThreads,
        IUnitOfWork unitOfWork,
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

        thread.Unarchive(command.ActorUserId, command.CorrelationId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
