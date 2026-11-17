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
    public static async Task<Result<Guid>> Handle(
        PurchaseAddOnCommand command,
        AddOnPurchaseEligibility eligibility,
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
        var replay = await ReplayAsync(command, tenantAddOns, ct);
        if (replay is not null)
            return replay;

        var validation = await eligibility.EnsurePurchasableAsync(
            command.TenantId,
            command.AddOnCode,
            command.Quantity,
            ct
        );
        if (validation.IsFailure)
            return Result.Failure<Guid>(validation.Error);

        var (subscription, definition) = validation.Value;
        var nowUtc = DateTime.UtcNow;

        var purchased = AddOnPurchasePolicy.Purchase(
            subscription,
            definition,
            command.Quantity,
            command.AutoRenew,
            command.RequestedByUserId,
            nowUtc,
            RequestedAddOnId(command),
            IdempotencyKeyFactory.AddOnInitialCharge
        );
        if (purchased.IsFailure)
            return Result.Failure<Guid>(purchased.Error);

        var (addOn, charge) = purchased.Value;

        await tenantAddOns.AddAsync(addOn, ct);

        // Guardar antes de publicar: un reintento en línea no debe dejar salir el cobro del intento fallido.
        await unitOfWork.SaveChangesAsync(ct);

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
        if (charge is not null)
            await bus.PublishAsync(RenewalDue(addOn, charge, subscription, correlation.CorrelationId));

        metrics.RecordAddOnPurchased(addOn.AddOnCode);
        if (charge is not null)
            metrics.RecordAddOnBilled(addOn.AddOnCode, charge.AmountCents);

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

    private static Guid? RequestedAddOnId(PurchaseAddOnCommand command) =>
        string.IsNullOrWhiteSpace(command.ClientRequestKey)
            ? null
            : PurchaseRequestIds.AddOn(command.TenantId, command.ClientRequestKey);

    // SUB-12: va antes de la elegibilidad. Un add-on de instancia única ya comprado no debe responder AlreadyActive.
    private static async Task<Result<Guid>?> ReplayAsync(
        PurchaseAddOnCommand command,
        ITenantAddOnRepository tenantAddOns,
        CancellationToken ct
    )
    {
        if (RequestedAddOnId(command) is not { } addOnId)
            return null;

        var existing = await tenantAddOns.GetByIdAsync(addOnId, command.TenantId, ct);
        if (existing is null)
            return null;

        var sameRequest =
            string.Equals(existing.AddOnCode, command.AddOnCode, StringComparison.OrdinalIgnoreCase)
            && existing.Quantity == command.Quantity;
        return sameRequest
            ? Result.Success(existing.Id)
            : Result.Failure<Guid>(
                new Error("AddOn.IdempotencyKeyReused", "This Idempotency-Key was already used for another purchase.")
            );
    }

    // Si el descuento vigente cubre add-ons, el cobro inicial (prorrateado) sale con descuento.
    private static AddOnRenewalDueIntegrationEvent RenewalDue(
        TenantAddOn addOn,
        InitialCharge charge,
        TenantSubscription subscription,
        string correlationId
    )
    {
        var discounted = DiscountedCharge.For(
            subscription,
            DiscountChargeKind.AddOns,
            charge.AmountCents,
            charge.Currency,
            DateTime.UtcNow
        );
        return new()
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
    }
}
