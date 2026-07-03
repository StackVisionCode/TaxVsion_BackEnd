using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

public sealed record ApplyPendingChangeCommand(Guid PendingChangeId);

public static class ApplyPendingChangeHandler
{
    public static async Task<Result> Handle(
        ApplyPendingChangeCommand cmd,
        IPendingChangeRepository pendingRepo,
        IPlanRepository planRepo,
        IModuleRepository moduleRepo,
        ISubscriptionModuleRepository subscriptionModuleRepo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<ApplyPendingChangeCommand> logger,
        CancellationToken ct)
    {
        var pendingChange = await pendingRepo.GetByIdWithSubscriptionAsync(cmd.PendingChangeId, ct);
        if (pendingChange is null)
            return Result.Failure(new Error("Subscription.NotFound", $"Pending change {cmd.PendingChangeId} not found."));

        if (pendingChange.IsApplied)
        {
            logger.LogWarning("Pending plan change already applied: {Id}", cmd.PendingChangeId);
            return Result.Success();
        }

        var subscription = pendingChange.Subscription;
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription not found for pending change."));

        // ── 1. Apply plan / billing-period change on the aggregate ────────────
        if (pendingChange.NewPlanId.HasValue)
        {
            var newPlan = await planRepo.GetByIdAsync(pendingChange.NewPlanId.Value, ct);
            if (newPlan is null)
                return Result.Failure(new Error("Plan.NotFound", $"New plan {pendingChange.NewPlanId} not found."));

            var newBasePrice = pendingChange.NewBillingPeriod.HasValue
                ? new Money(newPlan.GetBasePrice(pendingChange.NewBillingPeriod.Value), newPlan.Currency)
                : new Money(newPlan.GetBasePrice(subscription.BillingPeriod), newPlan.Currency);

            var applyResult = subscription.ApplyPlanChange(
                newPlan.Id,
                newPlan.Code,
                newPlan.Name,
                newPlan.IncludedSeats,
                pendingChange.NewBillingPeriod,
                newBasePrice);

            if (applyResult.IsFailure)
                return applyResult;
        }
        else if (pendingChange.NewBillingPeriod.HasValue)
        {
            // Period-only change: keep current plan, update period and price
            var currentPlan = await planRepo.GetByIdAsync(subscription.PlanId, ct);
            if (currentPlan is not null)
            {
                var newBasePrice = new Money(
                    currentPlan.GetBasePrice(pendingChange.NewBillingPeriod.Value),
                    currentPlan.Currency);

                subscription.ApplyPlanChange(
                    subscription.PlanId,
                    subscription.PlanCode,
                    subscription.PlanName,
                    subscription.IncludedSeats,
                    pendingChange.NewBillingPeriod,
                    newBasePrice);
            }
        }

        // ── 2. Apply module inclusions/exclusions ────────────────────────────
        var modulesAffected = new ModulesAffectedData();
        if (!string.IsNullOrEmpty(pendingChange.ModulesAffected))
        {
            try
            {
                modulesAffected = JsonSerializer.Deserialize<ModulesAffectedData>(pendingChange.ModulesAffected)
                    ?? new ModulesAffectedData();
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse ModulesAffected for {Id}", cmd.PendingChangeId);
            }
        }

        var subscriptionModules = (await subscriptionModuleRepo.GetBySubscriptionIdAsync(subscription.Id, ct)).ToList();

        foreach (var moduleId in modulesAffected.Removed)
            subscriptionModules.FirstOrDefault(s => s.ModuleId == moduleId)?.SetIncluded(false);

        foreach (var moduleId in modulesAffected.Added)
        {
            var existing = subscriptionModules.FirstOrDefault(s => s.ModuleId == moduleId);
            if (existing is not null)
            {
                existing.SetIncluded(true);
            }
            else if (await moduleRepo.ExistsAsync(moduleId, ct))
            {
                await subscriptionModuleRepo.AddAsync(
                    SubscriptionModule.Create(subscription.Id, moduleId, isIncluded: true), ct);
            }
            else
            {
                logger.LogWarning("Module {ModuleId} not found when applying pending change", moduleId);
            }
        }

        // ── 3. Mark change applied, persist, notify ──────────────────────────
        pendingChange.MarkApplied("System-PlanChangeApplied");

        await bus.PublishAsync(new TenantEntitlementsChangedIntegrationEvent
        {
            TenantId = subscription.TenantId,
            SubscriptionId = subscription.Id,
            TotalAvailableSeats = subscription.TotalAvailableSeats,
            CorrelationId = correlation.CorrelationId
        });

        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Pending plan change applied: {Id}. {Old} -> {New}",
            cmd.PendingChangeId, pendingChange.OldPlanName, pendingChange.NewPlanName);

        return Result.Success();
    }
}

file sealed class ModulesAffectedData
{
    public List<Guid> Added   { get; set; } = [];
    public List<Guid> Removed { get; set; } = [];
}
