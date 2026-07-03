using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

public sealed record ChangePlanCommand(
    Guid SubscriptionId,
    Guid? NewPlanId,
    BillingPeriod? NewBillingPeriod);

public sealed record ChangePlanResponse(
    Guid PendingChangeId,
    string CurrentPlan,
    string NewPlan,
    DateTime EffectiveAtUtc);

public static class ChangePlanHandler
{
    public static async Task<Result<ChangePlanResponse>> Handle(
        ChangePlanCommand cmd,
        ISubscriptionRepository subscriptionRepo,
        IPlanRepository planRepo,
        IPendingChangeRepository pendingRepo,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        if (cmd.NewPlanId is null && cmd.NewBillingPeriod is null)
            return Result.Failure<ChangePlanResponse>(
                new Error("Subscription.NoChange", "NewPlanId or NewBillingPeriod is required."));

        var subscription = await subscriptionRepo.GetByIdAsync(cmd.SubscriptionId, ct);
        if (subscription is null)
            return Result.Failure<ChangePlanResponse>(
                new Error("Subscription.NotFound", $"Subscription {cmd.SubscriptionId} not found."));

        var currentPlan = await planRepo.GetByIdAsync(subscription.PlanId, ct);
        if (currentPlan is null)
            return Result.Failure<ChangePlanResponse>(new Error("Plan.NotFound", "Current plan not found."));

        var newPlanId = cmd.NewPlanId ?? currentPlan.Id;
        var newPlan = await planRepo.GetByIdAsync(newPlanId, ct);
        if (newPlan is null)
            return Result.Failure<ChangePlanResponse>(new Error("Plan.NotFound", $"Plan {newPlanId} not found."));

        if (!newPlan.IsActive)
            return Result.Failure<ChangePlanResponse>(new Error("Plan.Inactive", "The target plan is not active."));

        var newBillingPeriod = cmd.NewBillingPeriod ?? subscription.BillingPeriod;
        var changeType = DetermineChangeType(currentPlan.Id, newPlan.Id, subscription.BillingPeriod, newBillingPeriod);

        var pendingChange = PendingSubscriptionChange.Create(
            subscriptionId:   subscription.Id,
            transactionId:    null,
            changeType:       changeType,
            oldPlanId:        currentPlan.Id,
            newPlanId:        newPlan.Id,
            oldPlanName:      currentPlan.Name,
            newPlanName:      newPlan.Name,
            oldBillingPeriod: subscription.BillingPeriod,
            newBillingPeriod: newBillingPeriod,
            oldPrice:         subscription.CurrentBasePrice.Amount,
            newPrice:         newPlan.GetPriceForPeriod(newBillingPeriod),
            modulesAffected:  null);

        await pendingRepo.AddAsync(pendingChange, ct);
        await uow.SaveChangesAsync(ct);

        return Result.Success(new ChangePlanResponse(
            PendingChangeId: pendingChange.Id,
            CurrentPlan:     $"{currentPlan.Name} ({subscription.BillingPeriod})",
            NewPlan:         $"{newPlan.Name} ({newBillingPeriod})",
            EffectiveAtUtc:  subscription.PeriodEndUtc));
    }

    private static string DetermineChangeType(Guid curPlanId, Guid newPlanId, BillingPeriod curPeriod, BillingPeriod newPeriod)
        => (curPlanId != newPlanId, curPeriod != newPeriod) switch
        {
            (true,  true)  => "PLAN_AND_PERIOD_CHANGE",
            (true,  false) => "PLAN_CHANGE",
            (false, true)  => "BILLING_PERIOD_CHANGE",
            _              => "PLAN_MODIFICATION"
        };
}
