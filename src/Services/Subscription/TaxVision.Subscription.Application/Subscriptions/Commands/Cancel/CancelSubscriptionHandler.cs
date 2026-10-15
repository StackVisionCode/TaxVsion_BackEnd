using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.Cancel;

public static class CancelSubscriptionHandler
{
    public static async Task<Result> Handle(
        CancelSubscriptionCommand command,
        ISubscriptionRepository subscriptions,
        IHostedCheckoutCancellationClient cancellation,
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

        var nowUtc = DateTime.UtcNow;
        var previousStatus = subscription.Status;
        var wasAlreadyScheduled = subscription.CancelAtPeriodEnd;

        // Cerrar upgrade AwaitingPayment antes de agendar: si no, Stripe cobra el upgrade igual mientras
        // el usuario cree que canceló.
        var cancelledUpgrade = subscription.CancelPendingUpgrade(command.RequestedByUserId, nowUtc, command.Reason);
        if (cancelledUpgrade.IsFailure)
            return Result.Failure(cancelledUpgrade.Error);

        var result = subscription.ScheduleCancellation(command.Reason, command.RequestedByUserId, nowUtc);
        if (result.IsFailure)
            return result;

        if (wasAlreadyScheduled && cancelledUpgrade.Value is null)
        {
            logger.LogInformation(
                "Tenant {TenantId} cancellation was already scheduled; skipping audit and publish.",
                command.TenantId
            );
            return Result.Success();
        }

        // Guardado y publicación en el mismo acto: Wolverine arma la outbox local alrededor del handler, así
        // que todos los PublishAsync y el SaveChanges viajan juntos.
        if (cancelledUpgrade.Value is { } upgradeRequest)
        {
            if (upgradeRequest.SaaSPaymentId is { } paymentId)
            {
                var providerResult = await cancellation.CancelAsync(command.TenantId, paymentId, ct);
                if (providerResult.IsFailure)
                    logger.LogWarning(
                        "Provider cancel for upgrade {RequestId} returned failure: {Error}; proceeding — local state is Cancelled.",
                        upgradeRequest.Id,
                        providerResult.Error.Code
                    );
            }

            await bus.PublishAsync(
                new PlanChangeUpgradeCancelledIntegrationEvent
                {
                    TenantId = command.TenantId,
                    PlanChangeRequestId = upgradeRequest.Id,
                    SaaSPaymentId = upgradeRequest.SaaSPaymentId,
                    ActorUserId = command.RequestedByUserId,
                    Reason = command.Reason,
                    CorrelationId = correlation.CorrelationId,
                }
            );

            await AuditEntryFactory.AppendAsync(
                audit,
                command.TenantId,
                "PlanChangeRequest",
                upgradeRequest.Id,
                "PlanChangeRequest.UpgradeCancelled",
                command.RequestedByUserId,
                correlation.CorrelationId,
                before: new { Status = PlanChangeRequestStatus.AwaitingPayment.ToString() },
                after: new { Status = upgradeRequest.Status.ToString() },
                reason: command.Reason,
                nowUtc,
                ct
            );
        }

        // El acceso NO cambia acá: sigue activo y pagado hasta el fin del período, así que no hay
        // entitlements que recalcular. El job de renovación expira y recalcula cuando llegue la fecha.
        await bus.PublishStatusChangedAsync(
            subscription,
            previousStatus,
            SubscriptionChangeReason.CancellationScheduled,
            command.RequestedByUserId,
            correlationId: correlation.CorrelationId
        );

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantSubscription",
            subscription.Id,
            "TenantSubscription.CancellationScheduled",
            command.RequestedByUserId,
            correlation.CorrelationId,
            before: new { Status = previousStatus.ToString(), CancelAtPeriodEnd = false },
            after: new
            {
                Status = subscription.Status.ToString(),
                CancelAtPeriodEnd = true,
                AccessEndsAtUtc = subscription.CurrentPeriodEndUtc,
            },
            reason: command.Reason,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Tenant {TenantId} scheduled its cancellation for {AccessEndsAtUtc} (requested by {UserId}): {Reason}.",
            command.TenantId,
            subscription.CurrentPeriodEndUtc,
            command.RequestedByUserId,
            command.Reason
        );
        return Result.Success();
    }
}
