using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Catalog.Confirmation;
using TaxVision.Subscription.Application.Plans.Commands.PlanSales;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Plans;
using Wolverine;

namespace TaxVision.Subscription.Application.Plans.Commands.DeprecatePlan;

/// <summary>Operación peligrosa (fin de vida, de una sola vía): vista previa, token y motivo. También retira de la venta.</summary>
public static class DeprecatePlanHandler
{
    public static async Task<Result> Handle(
        DeprecatePlanCommand command,
        IPlanRepository plans,
        IPlanSubscriptionsAcrossTenants subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IOptions<SubscriptionOptions> options,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var result = await PlanChange.ApplyConfirmedAsync(
            new ConfirmedPlanOperation(
                command.PlanId,
                command.ActorUserId,
                PlanOperation.Deprecate,
                command.ConfirmationToken,
                command.Reason,
                "plan.deprecated"
            ),
            (plan, now) => plan.Deprecate(command.ActorUserId, now),
            plans,
            subscriptions,
            unitOfWork,
            options.Value,
            audit,
            correlation,
            ct
        );
        if (result.IsFailure)
            return result;

        await bus.PublishAsync(CatalogNotice.For("plan.deprecated", result.Value, correlation));
        return Result.Success();
    }
}

/// <summary>Operación peligrosa: vista previa, token y motivo. Solo un plan deprecado y sin oficinas.</summary>
public static class ArchivePlanHandler
{
    public static async Task<Result> Handle(
        ArchivePlanCommand command,
        IPlanRepository plans,
        IPlanSubscriptionsAcrossTenants subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IOptions<SubscriptionOptions> options,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var result = await PlanChange.ApplyConfirmedAsync(
            new ConfirmedPlanOperation(
                command.PlanId,
                command.ActorUserId,
                PlanOperation.Archive,
                command.ConfirmationToken,
                command.Reason,
                "plan.archived"
            ),
            (plan, now) => plan.Archive(command.ActorUserId, now),
            plans,
            subscriptions,
            unitOfWork,
            options.Value,
            audit,
            correlation,
            ct
        );
        if (result.IsFailure)
            return result;

        await bus.PublishAsync(CatalogNotice.For("plan.archived", result.Value, correlation));
        return Result.Success();
    }
}

/// <summary>Aviso del catálogo para cambios que todavía se publican a mano (después de guardar).</summary>
public static class CatalogNotice
{
    public static CatalogChangedIntegrationEvent For(
        string changeKind,
        SubscriptionPlan plan,
        ICorrelationContext correlation
    ) =>
        new()
        {
            ChangeKind = changeKind,
            EntityId = plan.Id,
            EntityCode = plan.Code.Value,
            CorrelationId = correlation.CorrelationId,
        };
}
