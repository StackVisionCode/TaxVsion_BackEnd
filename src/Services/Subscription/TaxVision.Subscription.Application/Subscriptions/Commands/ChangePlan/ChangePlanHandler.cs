using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;

public static class ChangePlanHandler
{
    public static async Task<Result> Handle(
        ChangePlanCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        ISubscriptionTenantSettingsRepository settingsRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var plan = await plans.GetByCodeAsync(command.PlanCode?.Trim().ToLowerInvariant() ?? string.Empty, ct);
        if (plan is null || plan.Status != PlanStatus.Published)
            return Result.Failure(new Error("Plan.NotFound", "Plan does not exist."));

        var planVersion = plan.GetPublishedVersion();
        if (planVersion is null)
            return Result.Failure(new Error("Plan.NoPublishedVersion", "Plan has no published version."));

        if (subscription.PlanId == plan.Id && subscription.PlanVersionId == planVersion.Id)
            return Result.Success();

        var settings = await settingsRepository.GetByTenantIdAsync(command.TenantId, ct);
        var mode = settings?.PlanChangeEffective ?? PlanChangeEffectiveMode.Immediate;

        var nowUtc = DateTime.UtcNow;
        var previousPlanCode = subscription.PlanCode;

        var result = subscription.RequestPlanChange(plan, planVersion, mode, command.RequestedByUserId, nowUtc);
        if (result.IsFailure)
            return result;

        if (mode == PlanChangeEffectiveMode.Immediate)
        {
            await unitOfWork.SaveChangesAsync(ct);

            await AuditEntryFactory.AppendAsync(
                audit,
                command.TenantId,
                "TenantSubscription",
                subscription.Id,
                "TenantSubscription.PlanChanged",
                command.RequestedByUserId,
                correlation.CorrelationId,
                before: new { PlanCode = previousPlanCode },
                after: new { PlanCode = subscription.PlanCode },
                reason: null,
                nowUtc,
                ct
            );

            await bus.RecalculateEntitlementsSafelyAsync(command.TenantId, logger, ct);

            logger.LogInformation(
                "Tenant {TenantId} changed plan to {PlanCode} immediately (requested by {UserId}).",
                command.TenantId,
                plan.Code.Value,
                command.RequestedByUserId
            );
            return Result.Success();
        }

        await unitOfWork.SaveChangesAsync(ct);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantSubscription",
            subscription.Id,
            "TenantSubscription.PlanChangeRequested",
            command.RequestedByUserId,
            correlation.CorrelationId,
            before: new { PlanCode = previousPlanCode },
            after: new
            {
                PlanCode = previousPlanCode,
                PendingPlanCode = plan.Code.Value,
                EffectiveAtUtc = subscription.CurrentPeriodEndUtc,
            },
            reason: null,
            nowUtc,
            ct
        );

        logger.LogInformation(
            "Tenant {TenantId} queued plan change to {PlanCode}, effective at end of period (requested by {UserId}).",
            command.TenantId,
            plan.Code.Value,
            command.RequestedByUserId
        );
        return Result.Success();
    }
}
