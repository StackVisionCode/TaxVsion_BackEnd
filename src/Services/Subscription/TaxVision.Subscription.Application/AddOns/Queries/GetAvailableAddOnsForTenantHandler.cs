using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.AddOns.Queries;

public static class GetAvailableAddOnsForTenantHandler
{
    public static async Task<Result<IReadOnlyList<AvailableAddOnResponse>>> Handle(
        GetAvailableAddOnsForTenantQuery query,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IModuleRepository modules,
        IAddOnDefinitionRepository addOnDefinitions,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(query.TenantId, ct);
        if (subscription is null)
            return Fail("Subscription.NotFound", "Subscription does not exist.");

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Fail("Plan.NotFound", "Plan does not exist.");

        var version =
            plan.GetPublishedVersion()
            ?? plan.Versions.FirstOrDefault(candidate => candidate.Id == subscription.PlanVersionId);
        if (version is null)
            return Fail("Plan.NoPublishedVersion", "Plan has no published version.");

        var includedModules = PlanVersionEntitlements
            .GetEnabledModules(version)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var response = new List<AvailableAddOnResponse>();

        foreach (
            var module in (await modules.GetAllAsync(ct)).Where(module =>
                module.IsSellableAsAddon && !includedModules.Contains(module.Code)
            )
        )
        {
            response.Add(
                new AvailableAddOnResponse(
                    module.AddOnCode,
                    module.Name,
                    module.Description,
                    "module",
                    module.AddonPriceMonthlyUsd,
                    module.AddonPriceYearlyUsd,
                    null,
                    [module.Code]
                )
            );
        }

        foreach (var definition in (await addOnDefinitions.GetPublishedAsync(ct)).Where(IsQuota))
        {
            response.Add(
                new AvailableAddOnResponse(
                    definition.Code.Value,
                    definition.Name,
                    definition.Description,
                    "quota",
                    Price(definition, BillingCycle.Monthly),
                    Price(definition, BillingCycle.Yearly),
                    definition.MutexGroup,
                    []
                )
            );
        }

        return Result.Success<IReadOnlyList<AvailableAddOnResponse>>(
            response.OrderBy(addOn => addOn.Category).ThenBy(addOn => addOn.Name).ToArray()
        );
    }

    private static bool IsQuota(AddOnDefinition definition) =>
        string.Equals(definition.Category, "quota", StringComparison.OrdinalIgnoreCase);

    private static decimal? Price(AddOnDefinition definition, BillingCycle cycle)
    {
        var price = definition.ResolveUnitPrice(cycle, 1);
        return price.IsSuccess ? price.Value.Amount : null;
    }

    private static Result<IReadOnlyList<AvailableAddOnResponse>> Fail(string code, string message) =>
        Result.Failure<IReadOnlyList<AvailableAddOnResponse>>(new Error(code, message));
}
