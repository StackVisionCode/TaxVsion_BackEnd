using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Application.CustomerAccess.Queries.GetRulesByCustomer;

public static class GetRulesByCustomerHandler
{
    public static async Task<Result<IReadOnlyList<CustomerAccessRuleView>>> Handle(
        GetRulesByCustomerQuery query,
        ICustomerAccessRuleRepository rules,
        ICustomerAssignmentProjectionStore assignments,
        CancellationToken ct
    )
    {
        var auth = await EnsureAuthorized(query, assignments, ct);
        if (auth.IsFailure)
            return Result.Failure<IReadOnlyList<CustomerAccessRuleView>>(auth.Error);

        var raw = await rules.ListByCustomerAsync(query.TenantId, query.CustomerId, ct);
        IReadOnlyList<CustomerAccessRuleView> mapped = raw.Select(CustomerAccessRuleMapper.Map).ToList();
        return Result.Success(mapped);
    }

    private static async Task<Result> EnsureAuthorized(
        GetRulesByCustomerQuery query,
        ICustomerAssignmentProjectionStore assignments,
        CancellationToken ct
    )
    {
        if (query.HasManageAny)
            return Result.Success();
        if (!query.HasManageAssigned)
            return Result.Failure(CustomerAccessErrors.Forbidden);

        var assigned = await assignments.IsAnyAssignedToUserAsync(
            query.TenantId,
            query.ActorUserId,
            [query.CustomerId],
            ct
        );
        return assigned ? Result.Success() : Result.Failure(CustomerAccessErrors.Forbidden);
    }
}
