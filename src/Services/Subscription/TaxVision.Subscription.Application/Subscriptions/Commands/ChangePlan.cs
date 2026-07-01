using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

public record ChangePlanCommand(
    Guid SubscriptionId,
    Guid? NewPlanId,
    BillingPeriod? NewBillingPeriod);

public sealed record ChangePlanResponse(
    bool Success,
    string Message,
    Guid PendingChangeId,
    string CurrentPlan,
    string NewPlan,
    DateTime EffectiveAtUtc);

public static class ChangePlanHandler
{
    public static async Task<ChangePlanResponse> Handle(
        ChangePlanCommand cmd,
        ISubscriptionRepository subscriptionRepo,
        IPlanRepository planRepo,
        IPendingChangeRepository pendingRepo,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        if (cmd.NewPlanId == null && cmd.NewBillingPeriod == null)
            throw new ArgumentException("Se requiere al menos un cambio: NewPlanId o NewBillingPeriod.");

        var subscription = await subscriptionRepo.GetByIdAsync(cmd.SubscriptionId, ct)
            ?? throw new InvalidOperationException($"Suscripción {cmd.SubscriptionId} no encontrada.");

        var currentPlan = await planRepo.GetByIdAsync(subscription.PlanId, ct)
            ?? throw new InvalidOperationException("Plan actual no encontrado.");

        var newPlanId = cmd.NewPlanId ?? currentPlan.Id;
        var newPlan = await planRepo.GetByIdAsync(newPlanId, ct)
            ?? throw new InvalidOperationException($"Plan {newPlanId} no encontrado.");

        if (!newPlan.IsActive)
            throw new InvalidOperationException("El nuevo plan no está activo.");

        var newBillingPeriod = cmd.NewBillingPeriod ?? subscription.BillingPeriod;
        var changeType = DetermineChangeType(
            currentPlan.Id, newPlan.Id,
            subscription.BillingPeriod, newBillingPeriod);

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

        return new ChangePlanResponse(
            Success:         true,
            Message:         "Cambio de plan registrado. Aplicará en el próximo ciclo de renovación.",
            PendingChangeId: pendingChange.Id,
            CurrentPlan:     $"{currentPlan.Name} ({subscription.BillingPeriod})",
            NewPlan:         $"{newPlan.Name} ({newBillingPeriod})",
            EffectiveAtUtc:  subscription.PeriodEndUtc);
    }

    private static string DetermineChangeType(
        Guid curPlanId, Guid newPlanId,
        BillingPeriod curPeriod, BillingPeriod newPeriod)
        => (curPlanId != newPlanId, curPeriod != newPeriod) switch
        {
            (true, true)  => "PLAN_AND_PERIOD_CHANGE",
            (true, false) => "PLAN_CHANGE",
            (false, true) => "BILLING_PERIOD_CHANGE",
            _             => "PLAN_MODIFICATION"
        };
}
