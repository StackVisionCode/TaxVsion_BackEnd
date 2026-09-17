using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForPlan;
using Wolverine;

namespace TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForAllPlans;

/// <summary>Abanica un <see cref="RecalculateEntitlementsForPlanCommand"/> por cada plan publicado.
/// Reusa el fan-out por plan (keyset por tenant) — un solo comando reconcilia toda la flota.</summary>
public static class RecalculateEntitlementsForAllPlansHandler
{
    public static async Task<Result<int>> Handle(
        RecalculateEntitlementsForAllPlansCommand command,
        IPlanRepository plans,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var published = await plans.GetPublishedAsync(ct);
        foreach (var plan in published)
            await bus.PublishAsync(new RecalculateEntitlementsForPlanCommand(plan.Id));

        return Result.Success(published.Count);
    }
}
