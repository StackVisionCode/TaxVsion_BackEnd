using BuildingBlocks.Common;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Users;

public static class ReconciliationStaffUsersHandler
{
    public static Task<PagedResult<StaffUserReconciliationResponse>> Handle(
        ReconciliationStaffUsersQuery query,
        IUserRepository users,
        CancellationToken ct
    ) => users.ListStaffUsersForReconciliationAsync(query.Page, query.Size, ct);
}
