using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

public sealed record CreateSubscriptionCommand(
    Guid TenantId,
    ServiceLevel ServiceLevel,
    BillingPeriod BillingPeriod,
    bool IsActive = true,
    DateTime? StartDate = null);

public sealed record CreateSubscriptionResponse(
    Guid SubscriptionId,
    Guid TenantId,
    decimal Price,
    BillingPeriod BillingPeriod,
    DateTime StartDate,
    DateTime RenewDate);

public static class CreateSubscriptionHandler
{
    public static async Task<Result<CreateSubscriptionResponse>> Handle(
        CreateSubscriptionCommand cmd,
        ISubscriptionRepository subscriptionRepo,
        IPlanRepository planRepo,
        ISubscriptionModuleRepository subscriptionModuleRepo,
        IUnitOfWork uow,
        ILogger<CreateSubscriptionCommand> logger,
        CancellationToken ct)
    {
        if (await subscriptionRepo.ExistsForTenantAsync(cmd.TenantId, ct))
            return Result.Failure<CreateSubscriptionResponse>(
                new Error("Subscription.AlreadyExists", $"Tenant {cmd.TenantId} already has a subscription."));

        var plan = await planRepo.GetByServiceLevelAsync(cmd.ServiceLevel, ct);
        if (plan is null)
            return Result.Failure<CreateSubscriptionResponse>(
                new Error("Plan.NotFound", $"No active plan found for ServiceLevel {cmd.ServiceLevel}."));

        var start = cmd.StartDate?.ToUniversalTime() ?? DateTime.UtcNow;
        var periodPrice = plan.GetPriceForPeriod(cmd.BillingPeriod);
        var renewDate = cmd.BillingPeriod == BillingPeriod.Annual
            ? start.AddYears(1)
            : start.AddMonths(1);

        var subResult = Subscription.Activate(
            tenantId: cmd.TenantId,
            enrollmentId: Guid.NewGuid(),
            planId: plan.Id,
            planCode: plan.Code,
            planName: plan.Name,
            currentBasePrice: new Money(periodPrice, plan.Currency),
            currentPricePerSeat: new Money(plan.PricePerAdditionalSeat, plan.Currency),
            billingPeriod: cmd.BillingPeriod,
            includedSeats: plan.IncludedSeats,
            activatedAtUtc: start,
            autoRenew: true);

        if (subResult.IsFailure)
            return Result.Failure<CreateSubscriptionResponse>(subResult.Error);

        var subscription = subResult.Value;
        await subscriptionRepo.AddAsync(subscription, ct);

        foreach (var pm in plan.PlanModules)
        {
            var sm = SubscriptionModule.Create(subscription.Id, pm.ModuleId, isIncluded: true);
            await subscriptionModuleRepo.AddAsync(sm, ct);
        }

        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Subscription created: {SubId} for Tenant {TenantId}", subscription.Id, cmd.TenantId);

        return Result.Success(new CreateSubscriptionResponse(
            SubscriptionId: subscription.Id,
            TenantId: cmd.TenantId,
            Price: periodPrice,
            BillingPeriod: cmd.BillingPeriod,
            StartDate: start,
            RenewDate: renewDate));
    }
}
