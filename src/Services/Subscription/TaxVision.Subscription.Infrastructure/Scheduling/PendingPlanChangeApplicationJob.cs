using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions;
using BuildingBlocks.Results;
using Wolverine;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Aplica los cambios de plan diferidos (<see cref="PlanChangeRequest"/> en EndOfPeriod) cuya
/// EffectiveAtUtc ya llegó. No calcula prorrateo: simplemente cambia de plan y el precio nuevo
/// se cobra con normalidad en la próxima renovación.
/// </summary>
public sealed class PendingPlanChangeApplicationJob(
    IServiceScopeFactory scopeFactory, IDistributedLockFactory lockFactory, ILogger<PendingPlanChangeApplicationJob> logger)
    : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    protected override string JobName => "pending-plan-change-application";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var plans = services.GetRequiredService<IPlanRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<PendingPlanChangeApplicationJob>>();

        var nowUtc = DateTime.UtcNow;
        var due = await subscriptions.GetWithDuePlanChangeRequestsAsync(nowUtc, BatchSize, ct);

        var appliedCount = 0;
        foreach (var subscription in due)
        {
            var request = subscription.PlanChangeRequests.FirstOrDefault(
                r => r.Status == PlanChangeRequestStatus.Pending && r.EffectiveAtUtc <= nowUtc);
            if (request is null)
                continue;

            var toPlan = await plans.GetByIdAsync(request.ToPlanId, ct);
            var toPlanVersion = toPlan?.Versions.FirstOrDefault(v => v.Id == request.ToPlanVersionId);
            if (toPlan is null || toPlanVersion is null)
            {
                logger.LogWarning(
                    "Could not apply pending plan change {RequestId} for subscription {SubscriptionId}: target plan/version no longer exists.",
                    request.Id, subscription.Id);
                continue;
            }

            var result = subscription.ApplyPendingPlanChange(request.Id, toPlan, toPlanVersion, actorUserId: Guid.Empty, nowUtc);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not apply pending plan change {RequestId} for subscription {SubscriptionId}: {Code}.",
                    request.Id, subscription.Id, result.Error.Code);
                continue;
            }

            await unitOfWork.SaveChangesAsync(ct);

            await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(subscription.TenantId), ct);
            appliedCount++;
        }

        if (appliedCount > 0)
            logger.LogInformation("PendingPlanChangeApplicationJob applied {Count} deferred plan change(s).", appliedCount);
    }
}
