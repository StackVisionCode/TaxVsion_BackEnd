using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Subscription.Application.AddOns.TierChange;

/// <summary>Cambiar el tamaño de un paquete: subir se cobra a la tarjeta guardada; bajar queda para la renovación.</summary>
public sealed record ChangeAddOnTierCommand(
    Guid TenantId,
    Guid TenantAddOnId,
    string TargetCode,
    Guid RequestedByUserId
);

/// <summary>Paso transaccional del cambio, ya medido el espacio cuando es una bajada.</summary>
public sealed record ApplyAddOnTierChangeCommand(
    Guid TenantId,
    Guid TenantAddOnId,
    string TargetCode,
    Guid RequestedByUserId
);

/// <summary><paramref name="Direction"/>: Upgrade | Downgrade. Un Downgrade aplica en <paramref name="EffectiveAtUtc"/>.</summary>
public sealed record AddOnTierChangeResponse(
    string Direction,
    long ChargedCents,
    string Currency,
    DateTime EffectiveAtUtc
);

/// <summary>Orquesta sin transacción: una bajada mide el espacio en CloudStorage antes de escribir (BB-8).</summary>
public static class ChangeAddOnTierHandler
{
    [NonTransactional]
    public static async Task<Result<AddOnTierChangeResponse>> Handle(
        ChangeAddOnTierCommand command,
        AddOnTierChangeLoader loader,
        IStorageUsageClient usage,
        ITenantEntitlementSnapshotRepository snapshots,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var loaded = await loader.LoadAsync(
            command.TenantId,
            command.TenantAddOnId,
            command.TargetCode,
            DateTime.UtcNow,
            ct
        );
        if (loaded.IsFailure)
            return Result.Failure<AddOnTierChangeResponse>(loaded.Error);

        if (loaded.Value.Plan.Direction == AddOnTierChangeDirection.Downgrade)
        {
            var fits = await AddOnTierChangeStorage.EnsureFitsAsync(loaded.Value, usage, snapshots, ct);
            if (fits.IsFailure)
                return Result.Failure<AddOnTierChangeResponse>(fits.Error);
        }

        return await bus.InvokeAsync<Result<AddOnTierChangeResponse>>(
            new ApplyAddOnTierChangeCommand(
                command.TenantId,
                command.TenantAddOnId,
                command.TargetCode,
                command.RequestedByUserId
            ),
            ct
        );
    }
}

public static class ApplyAddOnTierChangeHandler
{
    public static async Task<Result<AddOnTierChangeResponse>> Handle(
        ApplyAddOnTierChangeCommand command,
        AddOnTierChangeLoader loader,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ISubscriptionDiscountReader discounts,
        ILogger<TenantAddOn> logger,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var loaded = await loader.LoadAsync(command.TenantId, command.TenantAddOnId, command.TargetCode, nowUtc, ct);
        if (loaded.IsFailure)
            return Result.Failure<AddOnTierChangeResponse>(loaded.Error);

        var context = loaded.Value;
        var changed =
            context.Plan.Direction == AddOnTierChangeDirection.Upgrade
                ? await UpgradeAsync(context, command, unitOfWork, bus, correlation, discounts, nowUtc, ct)
                : await ScheduleDowngradeAsync(context, command, unitOfWork, nowUtc, ct);
        if (changed.IsFailure)
            return changed;

        await AuditAsync(audit, context, command, correlation.CorrelationId, nowUtc, ct);
        await bus.RecalculateEntitlementsSafelyAsync(command.TenantId, logger, ct);
        return changed;
    }

    // Sube ya; la diferencia prorrateada sale como cobro off-session (guardar antes de publicar).
    private static async Task<Result<AddOnTierChangeResponse>> UpgradeAsync(
        AddOnTierChangeContext context,
        ApplyAddOnTierChangeCommand command,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionDiscountReader discounts,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var (addOn, _, _, target, plan) = context;
        var upgraded = addOn.UpgradeTier(target, plan, command.RequestedByUserId, nowUtc);
        if (upgraded.IsFailure)
            return Result.Failure<AddOnTierChangeResponse>(upgraded.Error);

        var charge = plan.ChargeNow.Cents > 0 ? InitialCharge.From(UpgradeKey(addOn, target), plan.ChargeNow) : null;
        if (charge is not null)
        {
            var begun = addOn.BeginInitialCharge(charge.IdempotencyKey, command.RequestedByUserId, nowUtc);
            if (begun.IsFailure)
                return Result.Failure<AddOnTierChangeResponse>(begun.Error);
        }

        await unitOfWork.SaveChangesAsync(ct);
        if (charge is not null)
        {
            // La diferencia de la subida lleva el descuento vigente si cubre add-ons.
            var discounted = await DiscountedCharge.ForAsync(
                discounts,
                addOn.TenantId,
                DiscountChargeKind.AddOns,
                charge.AmountCents,
                charge.Currency,
                nowUtc,
                ct
            );
            await bus.PublishAsync(ChargeDue(addOn, charge, discounted, correlation.CorrelationId));
        }

        return Response(plan, charge?.AmountCents ?? 0);
    }

    private static async Task<Result<AddOnTierChangeResponse>> ScheduleDowngradeAsync(
        AddOnTierChangeContext context,
        ApplyAddOnTierChangeCommand command,
        IUnitOfWork unitOfWork,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var scheduled = context.AddOn.ScheduleTierChange(
            context.Target,
            context.Plan,
            command.RequestedByUserId,
            nowUtc
        );
        if (scheduled.IsFailure)
            return Result.Failure<AddOnTierChangeResponse>(scheduled.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Response(context.Plan, 0);
    }

    internal static string UpgradeKey(TenantAddOn addOn, AddOnDefinition target) =>
        IdempotencyKeyFactory.AddOnTierUpgrade(addOn.Id, target.Id, addOn.CurrentPeriodStartUtc);

    private static Result<AddOnTierChangeResponse> Response(AddOnTierChangePlan plan, long chargedCents) =>
        Result.Success(
            new AddOnTierChangeResponse(
                plan.Direction.ToString(),
                chargedCents,
                plan.TargetUnitPrice.Currency,
                plan.EffectiveAtUtc
            )
        );

    private static AddOnRenewalDueIntegrationEvent ChargeDue(
        TenantAddOn addOn,
        InitialCharge charge,
        DiscountedCharge discounted,
        string correlationId
    ) =>
        new()
        {
            TenantId = addOn.TenantId,
            CorrelationId = correlationId,
            TenantAddOnId = addOn.Id,
            AddOnCode = addOn.AddOnCode,
            PeriodStartUtc = addOn.CurrentPeriodStartUtc,
            PeriodEndUtc = addOn.CurrentPeriodEndUtc,
            IdempotencyKey = charge.IdempotencyKey,
            AmountCents = discounted.NetCents,
            Currency = charge.Currency,
            DiscountAmountCents = discounted.DiscountCents,
            DiscountSource = discounted.Source,
            DiscountLabel = discounted.Label,
        };

    private static Task AuditAsync(
        ISubscriptionAuditLogWriter audit,
        AddOnTierChangeContext context,
        ApplyAddOnTierChangeCommand command,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    ) =>
        AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantAddOn",
            context.AddOn.Id,
            context.Plan.Direction == AddOnTierChangeDirection.Upgrade
                ? "AddOn.TierUpgraded"
                : "AddOn.TierChangeScheduled",
            command.RequestedByUserId,
            correlationId,
            before: new { AddOnCode = context.Current.Code.Value },
            after: new
            {
                AddOnCode = context.Target.Code.Value,
                ChargeNowCents = context.Plan.ChargeNow.Cents,
                context.Plan.EffectiveAtUtc,
            },
            reason: null,
            nowUtc,
            ct
        );
}
