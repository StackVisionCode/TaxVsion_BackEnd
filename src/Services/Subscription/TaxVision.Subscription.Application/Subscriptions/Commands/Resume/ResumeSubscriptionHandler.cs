using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.Resume;

/// <summary>
/// Deshace una cancelación programada. No cobra nada y no reactiva nada: la suscripción nunca dejó de estar
/// activa — solo se quita la marca de "termina al fin del período". Una suscripción ya expirada no se
/// resume: eso es el checkout de renovación.
/// </summary>
public static class ResumeSubscriptionHandler
{
    public static async Task<Result> Handle(
        ResumeSubscriptionCommand command,
        ISubscriptionRepository subscriptions,
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

        var result = subscription.ResumeCancellation(command.RequestedByUserId, nowUtc);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantSubscription",
            subscription.Id,
            "TenantSubscription.CancellationResumed",
            command.RequestedByUserId,
            correlation.CorrelationId,
            before: new { CancelAtPeriodEnd = true },
            after: new { CancelAtPeriodEnd = false },
            reason: null,
            nowUtc,
            ct
        );

        await bus.PublishStatusChangedAsync(
            subscription,
            previousStatus,
            SubscriptionChangeReason.CancellationResumed,
            command.RequestedByUserId,
            correlationId: correlation.CorrelationId
        );

        logger.LogInformation(
            "Tenant {TenantId} resumed its subscription before the scheduled cancellation (requested by {UserId}).",
            command.TenantId,
            command.RequestedByUserId
        );
        return Result.Success();
    }
}
