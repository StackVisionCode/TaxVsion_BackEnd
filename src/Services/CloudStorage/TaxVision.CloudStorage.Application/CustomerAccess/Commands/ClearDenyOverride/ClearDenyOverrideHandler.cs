using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Application.CustomerAccess.Commands.ClearDenyOverride;

public static class ClearDenyOverrideHandler
{
    public static async Task<Result<ClearDenyOverrideResponse>> Handle(
        ClearDenyOverrideCommand command,
        ICustomerAccessRuleRepository rules,
        ICustomerAccessRuleAuditRepository audit,
        ICustomerAssignmentProjectionStore assignments,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var auth = await EnsureAuthorized(command, assignments, ct);
        if (auth.IsFailure)
            return Result.Failure<ClearDenyOverrideResponse>(auth.Error);

        var existing = await rules.GetAsync(
            command.TenantId,
            command.CustomerId,
            command.ResourceType,
            command.ResourceId,
            command.Capability,
            ct
        );

        if (existing is null)
            return Result.Success(new ClearDenyOverrideResponse(false));

        rules.Remove(existing);
        audit.Add(
            CustomerAccessRuleAudit.Removed(
                existing.TenantId,
                existing.CustomerId,
                existing.ResourceType,
                existing.ResourceId,
                existing.Capability,
                command.ActorUserId,
                clock.UtcNow,
                existing.Reason
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new ClearDenyOverrideResponse(true));
    }

    private static async Task<Result> EnsureAuthorized(
        ClearDenyOverrideCommand command,
        ICustomerAssignmentProjectionStore assignments,
        CancellationToken ct
    )
    {
        if (command.HasManageAny)
            return Result.Success();
        if (!command.HasManageAssigned)
            return Result.Failure(CustomerAccessErrors.Forbidden);

        var assigned = await assignments.IsAnyAssignedToUserAsync(
            command.TenantId,
            command.ActorUserId,
            [command.CustomerId],
            ct
        );
        return assigned ? Result.Success() : Result.Failure(CustomerAccessErrors.Forbidden);
    }
}
