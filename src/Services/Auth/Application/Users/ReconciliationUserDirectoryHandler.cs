using BuildingBlocks.Common;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Users;

public static class ReconciliationUserDirectoryHandler
{
    public static Task<PagedResult<UserDirectoryReconciliationResponse>> Handle(
        ReconciliationUserDirectoryQuery query,
        IUserRepository users,
        CancellationToken ct
    ) => users.ListUsersForDirectoryReconciliationAsync(query.Page, query.Size, ct);
}
