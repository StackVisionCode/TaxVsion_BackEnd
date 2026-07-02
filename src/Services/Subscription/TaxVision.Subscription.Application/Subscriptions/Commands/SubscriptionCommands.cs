using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

// ---------------------------------------------------------------------------
// Upgrade / downgrade de plan
// ---------------------------------------------------------------------------

public sealed record ChangePlanCommand(Guid TenantId, string PlanCode, Guid RequestedByUserId);

public static class ChangePlanHandler
{
    public static async Task<Result> Handle(
        ChangePlanCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct)
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var plan = await plans.GetByCodeAsync(command.PlanCode?.Trim().ToLowerInvariant() ?? string.Empty, ct);
        if (plan is null || !plan.IsActive)
            return Result.Failure(new Error("Plan.NotFound", "Plan does not exist."));

        if (subscription.PlanId == plan.Id)
            return Result.Success();

        var result = subscription.ChangePlan(plan);
        if (result.IsFailure)
            return result;

        await bus.PublishAsync(
            SubscriptionEventFactory.PlanChanged(subscription, plan, correlation.CorrelationId));
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Tenant {TenantId} changed plan to {PlanCode} (requested by {UserId}).",
            command.TenantId, plan.Code, command.RequestedByUserId);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Compra de asientos adicionales
// ---------------------------------------------------------------------------

public sealed record PurchaseSeatsCommand(Guid TenantId, int AdditionalSeats, Guid RequestedByUserId);

public static class PurchaseSeatsHandler
{
    public static async Task<Result> Handle(
        PurchaseSeatsCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct)
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var result = subscription.AddSeats(command.AdditionalSeats);
        if (result.IsFailure)
            return result;

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan does not exist."));

        await bus.PublishAsync(new SeatsPurchasedIntegrationEvent
        {
            TenantId = subscription.TenantId,
            PurchasingTenantId = subscription.TenantId,
            NewMaxUsers = subscription.EffectiveMaxUsers(plan),
            CorrelationId = correlation.CorrelationId
        });
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Tenant {TenantId} purchased {Seats} extra seats (total effective {Total}).",
            command.TenantId, command.AdditionalSeats, subscription.EffectiveMaxUsers(plan));
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Cancelación por el tenant
// ---------------------------------------------------------------------------

public sealed record CancelSubscriptionCommand(Guid TenantId, Guid RequestedByUserId);

public static class CancelSubscriptionHandler
{
    public static async Task<Result> Handle(
        CancelSubscriptionCommand command,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct)
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var result = subscription.Cancel();
        if (result.IsFailure)
            return result;

        await bus.PublishAsync(new SubscriptionSuspendedIntegrationEvent
        {
            TenantId = subscription.TenantId,
            SubscribedTenantId = subscription.TenantId,
            Reason = "cancelled",
            CorrelationId = correlation.CorrelationId
        });
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Tenant {TenantId} cancelled its subscription (requested by {UserId}).",
            command.TenantId, command.RequestedByUserId);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Suspensión / reactivación (PlatformAdmin o Billing por impago)
// ---------------------------------------------------------------------------

public sealed record SuspendSubscriptionCommand(Guid TenantId, string Reason);

public static class SuspendSubscriptionHandler
{
    public static async Task<Result> Handle(
        SuspendSubscriptionCommand command,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct)
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var result = subscription.Suspend(command.Reason);
        if (result.IsFailure)
            return result;

        await bus.PublishAsync(new SubscriptionSuspendedIntegrationEvent
        {
            TenantId = subscription.TenantId,
            SubscribedTenantId = subscription.TenantId,
            Reason = command.Reason,
            CorrelationId = correlation.CorrelationId
        });
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogWarning(
            "Subscription suspended for tenant {TenantId}: {Reason}.",
            command.TenantId, command.Reason);
        return Result.Success();
    }
}

public sealed record ReactivateSubscriptionCommand(Guid TenantId);

public static class ReactivateSubscriptionHandler
{
    public static async Task<Result> Handle(
        ReactivateSubscriptionCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct)
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var result = subscription.Reactivate();
        if (result.IsFailure)
            return result;

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan does not exist."));

        await bus.PublishAsync(
            SubscriptionEventFactory.Activated(subscription, plan, correlation.CorrelationId));
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Subscription reactivated for tenant {TenantId}.", command.TenantId);
        return Result.Success();
    }
}
