using BuildingBlocks.Common;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Users;

public static class ReconciliationTenantOwnersHandler
{
    public static Task<PagedResult<TenantOwnerReconciliationResponse>> Handle(
        ReconciliationTenantOwnersQuery query,
        IUserRepository users,
        CancellationToken ct
    ) => users.ListTenantOwnersForReconciliationAsync(query.Page, query.Size, ct);
}
