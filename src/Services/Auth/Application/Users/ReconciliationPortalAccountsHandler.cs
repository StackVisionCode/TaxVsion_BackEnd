using BuildingBlocks.Common;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Users;

public static class ReconciliationPortalAccountsHandler
{
    public static Task<PagedResult<CustomerPortalAccountReconciliationResponse>> Handle(
        ReconciliationPortalAccountsQuery query,
        IUserRepository users,
        CancellationToken ct
    ) => users.ListPortalAccountsForReconciliationAsync(query.Page, query.Size, ct);
}
