using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Catalog.Confirmation;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Plans.Commands.PlanSales;

// Cargan, piden al plan, auditan y guardan. El aviso del catálogo sale de los eventos de dominio del plan.

public static class PutPlanOnSaleHandler
{
    public static async Task<Result> Handle(
        PutPlanOnSaleCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        await PlanChange.ApplyAsync(
            command.PlanId,
            command.ActorUserId,
            "plan.put_on_sale",
            (plan, now) => plan.PutOnSale(command.ActorUserId, now),
            plans,
            unitOfWork,
            audit,
            correlation,
            ct
        );
}

/// <summary>Operación peligrosa: exige la vista previa (token) y un motivo.</summary>
public static class WithdrawPlanFromSaleHandler
{
    public static async Task<Result> Handle(
        WithdrawPlanFromSaleCommand command,
        IPlanRepository plans,
        IPlanSubscriptionsAcrossTenants subscriptions,
        IUnitOfWork unitOfWork,
        IOptions<SubscriptionOptions> options,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        await PlanChange.ApplyConfirmedAsync(
            new ConfirmedPlanOperation(
                command.PlanId,
                command.ActorUserId,
                PlanOperation.WithdrawFromSale,
                command.ConfirmationToken,
                command.Reason,
                "plan.withdrawn_from_sale"
            ),
            (plan, now) => plan.WithdrawFromSale(command.ActorUserId, now),
            plans,
            subscriptions,
            unitOfWork,
            options.Value,
            audit,
            correlation,
            ct
        );
}

public static class SetPlanSalesRankHandler
{
    public static async Task<Result> Handle(
        SetPlanSalesRankCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var rank = SalesRank.Create(command.Rank);
        if (rank.IsFailure)
            return Result.Failure(rank.Error);

        return await PlanChange.ApplyAsync(
            command.PlanId,
            command.ActorUserId,
            "plan.rank",
            (plan, now) => plan.SetSalesRank(rank.Value, command.ActorUserId, now),
            plans,
            unitOfWork,
            audit,
            correlation,
            ct
        );
    }
}

public static class SetPlanFeaturedHandler
{
    public static async Task<Result> Handle(
        SetPlanFeaturedCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        await PlanChange.ApplyAsync(
            command.PlanId,
            command.ActorUserId,
            "plan.featured",
            (plan, now) => plan.SetFeatured(command.IsFeatured, command.ActorUserId, now),
            plans,
            unitOfWork,
            audit,
            correlation,
            ct
        );
}

/// <summary>Datos de una operación peligrosa confirmada.</summary>
public sealed record ConfirmedPlanOperation(
    Guid PlanId,
    Guid ActorUserId,
    PlanOperation Operation,
    string? ConfirmationToken,
    string? Reason,
    string AuditAction
);

/// <summary>
/// Pasos comunes de un cambio de plan del admin: cargar, foto de antes, cambiar, foto de después, auditar si cambió
/// algo y guardar en la misma transacción. Devuelve el plan para quien tenga que avisar algo después de guardar.
/// </summary>
public static class PlanChange
{
    private static readonly Error PlanNotFound = new("Plan.NotFound", "Plan not found.");

    public static async Task<Result<SubscriptionPlan>> ApplyAsync(
        Guid planId,
        Guid actorUserId,
        string action,
        Func<SubscriptionPlan, DateTime, Result> change,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(planId, ct);
        if (plan is null)
            return Result.Failure<SubscriptionPlan>(PlanNotFound);

        return await ChangeAndAuditAsync(plan, actorUserId, action, null, change, unitOfWork, audit, correlation, ct);
    }

    public static async Task<Result<SubscriptionPlan>> ApplyConfirmedAsync(
        ConfirmedPlanOperation operation,
        Func<SubscriptionPlan, DateTime, Result> change,
        IPlanRepository plans,
        IPlanSubscriptionsAcrossTenants subscriptions,
        IUnitOfWork unitOfWork,
        SubscriptionOptions options,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var request = PlanOperationConfirmation.ValidateRequest(operation.ConfirmationToken, operation.Reason);
        if (request.IsFailure)
            return Result.Failure<SubscriptionPlan>(request.Error);

        var plan = await plans.GetByIdForUpdateAsync(operation.PlanId, ct);
        if (plan is null)
            return Result.Failure<SubscriptionPlan>(PlanNotFound);

        var confirmed = await PlanOperationConfirmation.ConfirmAsync(
            plan,
            operation.Operation,
            operation.ConfirmationToken!,
            plans,
            subscriptions,
            options,
            ct
        );
        if (confirmed.IsFailure)
            return Result.Failure<SubscriptionPlan>(confirmed.Error);

        return await ChangeAndAuditAsync(
            plan,
            operation.ActorUserId,
            operation.AuditAction,
            operation.Reason!.Trim(),
            change,
            unitOfWork,
            audit,
            correlation,
            ct
        );
    }

    private static async Task<Result<SubscriptionPlan>> ChangeAndAuditAsync(
        SubscriptionPlan plan,
        Guid actorUserId,
        string action,
        string? reason,
        Func<SubscriptionPlan, DateTime, Result> change,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var before = CatalogAuditViews.Of(plan);
        var result = change(plan, nowUtc);
        if (result.IsFailure)
            return Result.Failure<SubscriptionPlan>(result.Error);

        await CatalogAudit.RecordIfChangedAsync(
            audit,
            CatalogAuditActor.ForUser(actorUserId),
            action,
            CatalogAuditViews.Target(plan),
            before,
            CatalogAuditViews.Of(plan),
            reason,
            correlation.CorrelationId,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(plan);
    }
}
