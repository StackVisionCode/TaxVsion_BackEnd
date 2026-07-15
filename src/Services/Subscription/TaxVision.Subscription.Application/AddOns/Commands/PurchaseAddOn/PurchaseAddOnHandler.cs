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
        IAddOnDefinitionRepository addOnDefinitions,
        ISubscriptionTenantSettingsRepository settingsRepository,
        ITenantAddOnRepository tenantAddOns,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<TenantAddOn> logger,
        CancellationToken ct
    )
    {
        var validation = await ValidateRequestAsync(command, subscriptions, addOnDefinitions, settingsRepository, ct);
        if (validation.IsFailure)
            return Result.Failure<Guid>(validation.Error);

        var definition = validation.Value;
        var nowUtc = DateTime.UtcNow;

        // Precio real pendiente de integración con Billing (fuera del bounded context de
        // Subscription); se persiste en 0 hasta que exista un catálogo de precios (Fase 5+).
        var addOnResult = TenantAddOn.Purchase(
            command.TenantId,
            definition,
            command.Quantity,
            Money.Zero("USD"),
            BillingCycle.Monthly,
            command.AutoRenew,
            command.RequestedByUserId,
            nowUtc
        );
        if (addOnResult.IsFailure)
            return Result.Failure<Guid>(addOnResult.Error);

        var addOn = addOnResult.Value;
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
        await unitOfWork.SaveChangesAsync(ct);

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

    private static async Task<Result<AddOnDefinition>> ValidateRequestAsync(
        PurchaseAddOnCommand command,
        ISubscriptionRepository subscriptions,
        IAddOnDefinitionRepository addOnDefinitions,
        ISubscriptionTenantSettingsRepository settingsRepository,
        CancellationToken ct
    )
    {
        if (command.Quantity < 1)
            return Result.Failure<AddOnDefinition>(new Error("AddOn.InvalidQuantity", "Quantity must be at least 1."));

        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure<AddOnDefinition>(new Error("Subscription.NotFound", "Subscription does not exist."));

        if (Array.IndexOf(PurchasableStatuses, subscription.Status) < 0)
        {
            return Result.Failure<AddOnDefinition>(
                new Error(
                    "Subscription.CannotPurchaseAddOns",
                    $"Cannot purchase add-ons while subscription is {subscription.Status}."
                )
            );
        }

        var settings = await settingsRepository.GetByTenantIdAsync(command.TenantId, ct);
        if (settings is not null && !settings.AllowAddons)
            return Result.Failure<AddOnDefinition>(
                new Error("AddOn.NotAllowed", "This tenant does not allow add-on purchases.")
            );

        var definition = await addOnDefinitions.GetByCodeAsync(
            command.AddOnCode?.Trim().ToLowerInvariant() ?? string.Empty,
            ct
        );
        if (definition is null || definition.Status != AddOnDefinitionStatus.Published)
            return Result.Failure<AddOnDefinition>(new Error("AddOnDefinition.NotFound", "Add-on does not exist."));

        return Result.Success(definition);
    }
}
