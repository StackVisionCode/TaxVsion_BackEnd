using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Domain.Availability;

namespace TaxVision.Calendar.Application.Availability.Commands;

public sealed record RemoveBlockedTimeCommand(Guid TenantId, Guid ActingUserId, Guid BlockId, bool CanManageOthers);

public static class RemoveBlockedTimeHandler
{
    public static async Task<Result> Handle(
        RemoveBlockedTimeCommand command,
        IAvailabilityRepository availability,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var block = await availability.FindBlockAsync(command.TenantId, command.BlockId, ct);
        if (block is null)
            return Result.Failure(AvailabilityErrors.BlockNotFound);
        if (block.UserId != command.ActingUserId && !command.CanManageOthers)
            return Result.Failure(AvailabilityErrors.Forbidden);

        availability.RemoveBlock(block);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
