using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Domain.Availability;

namespace TaxVision.Calendar.Application.Availability.Commands;

public sealed record CreateBlockedTimeCommand(
    Guid TenantId,
    Guid ActingUserId,
    Guid UserId,
    bool CanManageOthers,
    DateTime StartUtc,
    DateTime EndUtc,
    string? Reason
);

/// <summary>Una ausencia (vacaciones, día libre, capacitación). Siempre bloquea la agenda.</summary>
public static class CreateBlockedTimeHandler
{
    public static async Task<Result<BlockedTimeResponse>> Handle(
        CreateBlockedTimeCommand command,
        IAvailabilityRepository availability,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.UserId != command.ActingUserId && !command.CanManageOthers)
            return Result.Failure<BlockedTimeResponse>(AvailabilityErrors.Forbidden);

        var block = BlockedTime.Create(
            command.TenantId,
            command.UserId,
            command.StartUtc,
            command.EndUtc,
            command.Reason,
            DateTime.UtcNow
        );
        if (block.IsFailure)
            return Result.Failure<BlockedTimeResponse>(block.Error);

        availability.AddBlock(block.Value);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(BlockedTimeResponse.From(block.Value));
    }
}
