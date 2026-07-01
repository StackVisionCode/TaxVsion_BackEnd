using System.Text.Json;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

public record ApplyPendingChangeCommand(Guid PendingChangeId);

public static class ApplyPendingChangeHandler
{
    public static async Task<bool> Handle(
        ApplyPendingChangeCommand cmd,
        IPendingChangeRepository pendingRepo,
        IPlanRepository planRepo,
        IModuleRepository moduleRepo,
        ISubscriptionModuleRepository subscriptionModuleRepo,
        IUnitOfWork uow,
        ILogger<ApplyPendingChangeCommand> logger,
        CancellationToken ct)
    {
        logger.LogInformation("Applying pending plan change: {PendingChangeId}", cmd.PendingChangeId);

        var pendingChange = await pendingRepo.GetByIdWithSubscriptionAsync(cmd.PendingChangeId, ct)
            ?? throw new InvalidOperationException($"Pending plan change {cmd.PendingChangeId} not found.");

        if (pendingChange.IsApplied)
        {
            logger.LogWarning("Pending plan change already applied: {PendingChangeId}", cmd.PendingChangeId);
            return true;
        }

        var subscription = pendingChange.Subscription
            ?? throw new InvalidOperationException("Subscription not found for pending change.");

        var modulesAffected = new ModulesAffectedData();
        if (!string.IsNullOrEmpty(pendingChange.ModulesAffected))
        {
            try
            {
                modulesAffected = JsonSerializer.Deserialize<ModulesAffectedData>(
                    pendingChange.ModulesAffected) ?? new ModulesAffectedData();
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse ModulesAffected JSON for {PendingChangeId}", cmd.PendingChangeId);
            }
        }

        var subscriptionModules = (await subscriptionModuleRepo.GetBySubscriptionIdAsync(subscription.Id, ct)).ToList();

        if (pendingChange.NewPlanId.HasValue)
        {
            var newPlan = await planRepo.GetByIdAsync(pendingChange.NewPlanId.Value, ct);
            // newPlan?.IncludedSeats available if needed for future logic
        }

        foreach (var moduleId in modulesAffected.Removed)
        {
            var sm = subscriptionModules.FirstOrDefault(s => s.ModuleId == moduleId);
            sm?.SetIncluded(false);
        }

        foreach (var moduleId in modulesAffected.Added)
        {
            var existing = subscriptionModules.FirstOrDefault(s => s.ModuleId == moduleId);
            if (existing != null)
            {
                existing.SetIncluded(true);
            }
            else if (await moduleRepo.ExistsAsync(moduleId, ct))
            {
                var newSm = SubscriptionModule.Create(subscription.Id, moduleId, isIncluded: true);
                await subscriptionModuleRepo.AddAsync(newSm, ct);
            }
            else
            {
                logger.LogWarning("Module {ModuleId} not found when applying pending change", moduleId);
            }
        }

        pendingChange.MarkApplied("System-PlanChangeApplied");
        await uow.SaveChangesAsync(ct);

        logger.LogInformation(
            "Pending plan change applied: {PendingChangeId}. {OldPlan} -> {NewPlan}",
            cmd.PendingChangeId, pendingChange.OldPlanName, pendingChange.NewPlanName);

        return true;
    }
}

file sealed class ModulesAffectedData
{
    public List<Guid> Added { get; set; } = [];
    public List<Guid> Removed { get; set; } = [];
}
