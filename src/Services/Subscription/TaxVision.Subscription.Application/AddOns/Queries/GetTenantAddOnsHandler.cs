using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.AddOns.Queries;

public static class GetTenantAddOnsHandler
{
    public static async Task<Result<IReadOnlyList<AddOnResponse>>> Handle(
        GetTenantAddOnsQuery query,
        ITenantAddOnRepository tenantAddOns,
        CancellationToken ct
    )
    {
        var addOns = await tenantAddOns.GetByTenantIdAsync(query.TenantId, ct);

        var response = new List<AddOnResponse>(addOns.Count);
        foreach (var addOn in addOns)
        {
            response.Add(
                new AddOnResponse(
                    addOn.Id,
                    addOn.AddOnCode,
                    addOn.Status.ToString(),
                    addOn.Quantity,
                    addOn.BillingCycle.ToString(),
                    addOn.CurrentPeriodStartUtc,
                    addOn.CurrentPeriodEndUtc,
                    addOn.NextRenewalAtUtc,
                    addOn.AutoRenew,
                    addOn.PurchasedAtUtc
                )
            );
        }

        return Result.Success<IReadOnlyList<AddOnResponse>>(response);
    }
}
