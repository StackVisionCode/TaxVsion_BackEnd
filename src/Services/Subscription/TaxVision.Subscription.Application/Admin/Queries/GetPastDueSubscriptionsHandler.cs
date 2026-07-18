using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Admin.Queries;

public static class GetPastDueSubscriptionsHandler
{
    public static async Task<Result<PagedResult<AdminSubscriptionResponse>>> Handle(
        GetPastDueSubscriptionsQuery query,
        ISubscriptionRepository subscriptions,
        CancellationToken ct
    )
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var (items, totalCount) = await subscriptions.GetPastDueAsync(page, pageSize, ct);

        var response = new List<AdminSubscriptionResponse>(items.Count);
        foreach (var subscription in items)
            response.Add(
                new AdminSubscriptionResponse(
                    subscription.TenantId,
                    subscription.Id,
                    subscription.PlanCode,
                    subscription.Status.ToString(),
                    subscription.NextRenewalAtUtc
                )
            );

        return Result.Success(new PagedResult<AdminSubscriptionResponse>(response, page, pageSize, totalCount));
    }
}
