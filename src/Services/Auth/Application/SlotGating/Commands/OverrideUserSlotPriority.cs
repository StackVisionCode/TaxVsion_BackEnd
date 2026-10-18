using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.SlotGating.Commands;

public sealed record OverrideUserSlotPriorityCommand(
    Guid TenantId,
    Guid TargetUserId,
    Guid RequestedByUserId,
    UserActorType CallerActorType,
    DateTime? SlotPriorityAt,
    string Reason
);

public static class OverrideUserSlotPriorityHandler
{
    public static async Task<Result> Handle(
        OverrideUserSlotPriorityCommand command,
        IUserRepository users,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.CallerActorType != UserActorType.TenantAdmin)
            return Result.Failure(
                new Error("SeatSlots.AdminRequired", "Only a tenant administrator can reorder slots.")
            );

        var target = await users.GetByIdAsync(command.TargetUserId, ct);
        if (target is null)
            return Result.Failure(new Error("User.NotFound", "User was not found."));

        if (target.TenantId != command.TenantId)
            return Result.Failure(new Error("SeatSlots.CrossTenant", "The user belongs to another tenant."));

        if (!target.IsActive)
            return Result.Failure(new Error("Auth.UserInactive", "Inactive users do not occupy staff slots."));

        var previous = target.SlotPriorityAt;
        var slotPriorityAt = command.SlotPriorityAt?.ToUniversalTime();
        var overrideResult = target.OverrideSlotPriority(slotPriorityAt, command.RequestedByUserId, command.Reason);
        if (overrideResult.IsFailure)
            return overrideResult;

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.RequestedByUserId,
                AuthAuditAction.UserSlotPriorityOverridden,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "User",
                targetId: target.Id,
                detailsJson: JsonSerializer.Serialize(
                    new
                    {
                        PreviousSlotPriorityAt = previous,
                        SlotPriorityAt = slotPriorityAt,
                        Reason = command.Reason.Trim(),
                    }
                )
            ),
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
