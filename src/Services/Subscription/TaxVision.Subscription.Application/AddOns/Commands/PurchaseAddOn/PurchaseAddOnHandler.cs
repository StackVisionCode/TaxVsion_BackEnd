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

namespace TaxVision.Subscription.Application.AddOns.Commands.PurchaseAddOn;

public static class PurchaseAddOnHandler
{
    private static readonly SubscriptionStatus[] PurchasableStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active,
        SubscriptionStatus.GracePeriod,
    ];

    public static async Task<Result<Guid>> Handle(
        PurchaseAddOnCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IAddOnDefinitionRepository addOnDefinitions,
        ISubscriptionTenantSettingsRepository settingsRepository,
        ITenantAddOnRepository tenantAddOns,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ISubscriptionMetrics metrics,
        ILogger<TenantAddOn> logger,
        CancellationToken ct
    )
    {
        var validation = await AddOnPurchaseEligibility.EnsurePurchasableAsync(
            command.TenantId,
            command.AddOnCode,
            command.Quantity,
            subscriptions,
            plans,
            addOnDefinitions,
            settingsRepository,
            tenantAddOns,
            ct
        );
        if (validation.IsFailure)
            return Result.Failure<Guid>(validation.Error);

        var (subscription, definition) = validation.Value;
        var nowUtc = DateTime.UtcNow;

        // Co-terminación: el add-on hereda el ciclo del plan y su precio se resuelve para ese ciclo.
        var billingCycle = subscription.BillingCycle;
        var unitPrice = definition.ResolveUnitPrice(billingCycle, command.Quantity);
        if (unitPrice.IsFailure)
            return Result.Failure<Guid>(unitPrice.Error);

        var addOnResult = TenantAddOn.Purchase(
            command.TenantId,
            definition,
            command.Quantity,
            unitPrice.Value,
            billingCycle,
            command.AutoRenew,
            command.RequestedByUserId,
            nowUtc
        );
        if (addOnResult.IsFailure)
            return Result.Failure<Guid>(addOnResult.Error);

        var addOn = addOnResult.Value;

        // El primer período termina con el de la base; su cargo se proratea abajo.
        var coTerm = addOn.CoTermTo(subscription.CurrentPeriodEndUtc, command.RequestedByUserId, nowUtc);
        if (coTerm.IsFailure)
            return Result.Failure<Guid>(coTerm.Error);

        var initialCharge = PrepareInitialCharge(
            addOn,
            subscription,
            command.RequestedByUserId,
            nowUtc,
            correlation.CorrelationId
        );
        if (initialCharge.IsFailure)
            return Result.Failure<Guid>(initialCharge.Error);

        await tenantAddOns.AddAsync(addOn, ct);

        await bus.PublishAsync(
            new AddOnActivatedIntegrationEvent
            {
                TenantId = command.TenantId,
                TenantAddOnId = addOn.Id,
                AddOnCode = addOn.AddOnCode,
                Quantity = addOn.Quantity,
                CurrentPeriodEndUtc = addOn.CurrentPeriodEndUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );
        if (initialCharge.Value is not null)
            await bus.PublishAsync(initialCharge.Value);
        await unitOfWork.SaveChangesAsync(ct);

        metrics.RecordAddOnPurchased(addOn.AddOnCode);
        if (initialCharge.Value is not null)
            metrics.RecordAddOnBilled(addOn.AddOnCode, initialCharge.Value.AmountCents);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantAddOn",
            addOn.Id,
            "AddOn.Purchased",
            command.RequestedByUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                addOn.AddOnCode,
                addOn.Quantity,
                Status = addOn.Status.ToString(),
            },
            reason: null,
            nowUtc,
            ct
        );

        await bus.RecalculateEntitlementsSafelyAsync(command.TenantId, logger, ct);

        logger.LogInformation(
            "Tenant {TenantId} purchased add-on {AddOnCode} x{Quantity} (requested by {UserId}).",
            command.TenantId,
            addOn.AddOnCode,
            addOn.Quantity,
            command.RequestedByUserId
        );

        return Result.Success(addOn.Id);
    }

    // Cargo del período parcial inicial, prorrateado por días sobre el período vigente de la base.
    // La base no se proratea; esto aplica solo al add-on. Devuelve null si no hay nada que cobrar.
    private static Result<AddOnRenewalDueIntegrationEvent?> PrepareInitialCharge(
        TenantAddOn addOn,
        TenantSubscription subscription,
        Guid actorUserId,
        DateTime nowUtc,
        string correlationId
    )
    {
        var prorated = ProrationCalculator.InitialPeriod(
            addOn.UnitPrice,
            subscription.CurrentPeriodStartUtc,
            subscription.CurrentPeriodEndUtc,
            nowUtc
        );
        if (prorated.IsFailure)
            return Result.Failure<AddOnRenewalDueIntegrationEvent?>(prorated.Error);

        if (prorated.Value.Amount <= 0m)
            return Result.Success<AddOnRenewalDueIntegrationEvent?>(null);

        var key = IdempotencyKeyFactory.AddOnInitialCharge(addOn.Id, addOn.CurrentPeriodStartUtc);
        var begun = addOn.BeginInitialCharge(key, actorUserId, nowUtc);
        if (begun.IsFailure)
            return Result.Failure<AddOnRenewalDueIntegrationEvent?>(begun.Error);

        return Result.Success<AddOnRenewalDueIntegrationEvent?>(
            new AddOnRenewalDueIntegrationEvent
            {
                TenantId = addOn.TenantId,
                CorrelationId = correlationId,
                TenantAddOnId = addOn.Id,
                AddOnCode = addOn.AddOnCode,
                PeriodStartUtc = addOn.CurrentPeriodStartUtc,
                PeriodEndUtc = addOn.CurrentPeriodEndUtc,
                IdempotencyKey = key,
                AmountCents = (long)Math.Round(prorated.Value.Amount * 100m, MidpointRounding.AwayFromZero),
                Currency = prorated.Value.Currency,
            }
        );
    }
}
