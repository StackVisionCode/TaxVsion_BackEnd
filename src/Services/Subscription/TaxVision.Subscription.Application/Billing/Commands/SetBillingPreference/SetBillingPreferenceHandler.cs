using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Billing.Queries;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Billing;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Billing.Commands.SetBillingPreference;

/// <summary>Resuelve el modo pedido contra el aggregate. El monto/currency/cadence salen del plan vigente
/// del tenant — nunca del request — para que el frontend no pueda autorizar un cobro distinto al pactado.</summary>
public static class SetBillingPreferenceHandler
{
    public static async Task<Result<BillingPreferenceResponse>> Handle(
        SetBillingPreferenceCommand command,
        ITenantBillingPreferenceRepository repository,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantBillingPreference> logger,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<BillingMode>(command.Mode, ignoreCase: true, out var mode))
            return Result.Failure<BillingPreferenceResponse>(
                new Error("BillingPreference.InvalidMode", $"'{command.Mode}' is not a valid mode.")
            );

        if (mode == BillingMode.Automatic)
        {
            if (string.IsNullOrWhiteSpace(command.ProviderCode))
                return Result.Failure<BillingPreferenceResponse>(
                    new Error("BillingPreference.InvalidProvider", "ProviderCode is required for Automatic mode.")
                );
            if (string.IsNullOrWhiteSpace(command.ConsentText))
                return Result.Failure<BillingPreferenceResponse>(
                    new Error("BillingPreference.MissingConsentText", "ConsentText is required for Automatic mode.")
                );
            if (string.IsNullOrWhiteSpace(command.ConsentIpAddress))
                return Result.Failure<BillingPreferenceResponse>(
                    new Error("BillingPreference.MissingConsentIp", "ConsentIpAddress is required for Automatic mode.")
                );
        }

        // Precio/currency/cadence vienen del plan vigente — el request no los puede alterar.
        long amountCents = 0;
        var currency = "USD";
        var cadence = "monthly";
        if (mode == BillingMode.Automatic)
        {
            var resolved = await ResolveCurrentPricingAsync(subscriptions, plans, command.TenantId, ct);
            if (resolved.IsFailure)
                return Result.Failure<BillingPreferenceResponse>(resolved.Error);
            (amountCents, currency, cadence) = resolved.Value;
        }

        var nowUtc = DateTime.UtcNow;
        var existing = await repository.GetByTenantIdAsync(command.TenantId, ct);
        var hadActiveMandateBefore = existing?.ActiveMandate() is not null;
        var previousMode = existing?.Mode;

        TenantBillingPreference preference;
        if (existing is null)
        {
            var createResult = TenantBillingPreference.Choose(
                command.TenantId,
                mode,
                command.ProviderCode,
                command.ActorUserId,
                command.ConsentText,
                command.ConsentIpAddress,
                command.ConsentUserAgent,
                amountCents,
                currency,
                cadence,
                nowUtc
            );
            if (createResult.IsFailure)
                return Result.Failure<BillingPreferenceResponse>(createResult.Error);
            preference = createResult.Value;
            await repository.AddAsync(preference, ct);
        }
        else
        {
            preference = existing;
            var transition =
                mode == BillingMode.Automatic
                    ? preference.SwitchToAutomatic(
                        command.ProviderCode!,
                        command.ActorUserId,
                        command.ConsentText!,
                        command.ConsentIpAddress!,
                        command.ConsentUserAgent,
                        amountCents,
                        currency,
                        cadence,
                        nowUtc
                    )
                    : preference.SwitchToManual(command.ActorUserId, nowUtc);
            if (transition.IsFailure)
                return Result.Failure<BillingPreferenceResponse>(transition.Error);
        }

        await unitOfWork.SaveChangesAsync(ct);

        var active = preference.ActiveMandate();
        await bus.PublishAsync(BuildChangedEvent(preference, active, correlation.CorrelationId, nowUtc));

        var changedMode = previousMode is null || previousMode != preference.Mode;
        if (changedMode)
        {
            await bus.PublishAsync(
                BuildModeChangedStatusEvent(preference, active, command.TenantId, correlation.CorrelationId)
            );
        }
        if (preference.Mode == BillingMode.Automatic && active is not null && !hadActiveMandateBefore)
        {
            await bus.PublishAsync(
                BuildMandateCreatedStatusEvent(preference, active, command.TenantId, correlation.CorrelationId)
            );
        }

        logger.LogInformation(
            "Tenant {TenantId} set billing preference to {Mode} (provider={Provider}, mandate={Mandate}).",
            command.TenantId,
            preference.Mode,
            preference.ProviderCode ?? "-",
            active is not null
        );
        return Result.Success(
            new BillingPreferenceResponse(
                Mode: preference.Mode.ToString(),
                ProviderCode: preference.ProviderCode,
                HasActiveMandate: active is not null,
                AcceptedAtUtc: active?.AcceptedAtUtc,
                AgreedAmountCents: active?.AgreedAmountCents,
                AgreedCurrency: active?.AgreedCurrency,
                AgreedCadence: active?.AgreedCadence,
                ConsentTextVersion: active?.ConsentTextVersion
            )
        );
    }

    private static async Task<Result<(long AmountCents, string Currency, string Cadence)>> ResolveCurrentPricingAsync(
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(tenantId, ct);
        if (subscription is null)
            return Result.Failure<(long, string, string)>(
                new Error("BillingPreference.NoSubscription", "Tenant has no subscription to anchor the mandate to.")
            );

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Result.Failure<(long, string, string)>(
                new Error("BillingPreference.PlanNotFound", "Plan does not exist.")
            );

        var version = plan.GetPublishedVersion();
        if (version is null)
            return Result.Failure<(long, string, string)>(
                new Error("BillingPreference.NoPublishedVersion", "Plan has no published version.")
            );

        var price = PlanPricing.ResolveBaseSubscriptionPrice(version, subscription.BillingCycle);
        if (price is null || price.Value.AmountCents <= 0)
            return Result.Failure<(long, string, string)>(
                new Error("BillingPreference.NoPositivePrice", "Plan has no positive price for the current cycle.")
            );

        var cadence = subscription.BillingCycle == BillingCycle.Yearly ? "yearly" : "monthly";
        return Result.Success((price.Value.AmountCents, price.Value.Currency, cadence));
    }

    private static TenantBillingPreferenceChangedIntegrationEvent BuildChangedEvent(
        TenantBillingPreference preference,
        TenantPaymentMandate? active,
        string correlationId,
        DateTime nowUtc
    ) =>
        new()
        {
            TenantId = preference.TenantId,
            CorrelationId = correlationId,
            Mode = preference.Mode.ToString(),
            ProviderCode = preference.ProviderCode,
            HasActiveMandate = active is not null,
            AgreedAmountCents = active?.AgreedAmountCents,
            AgreedCurrency = active?.AgreedCurrency,
            AgreedCadence = active?.AgreedCadence,
            RevisionTicks = preference.UpdatedAtUtc.Ticks,
            ChangedAtUtc = nowUtc,
        };

    private static TenantSubscriptionStatusChangedIntegrationEvent BuildModeChangedStatusEvent(
        TenantBillingPreference preference,
        TenantPaymentMandate? active,
        Guid tenantId,
        string correlationId
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            TenantSubscriptionId = Guid.Empty,
            Status = "Active",
            PreviousStatus = "Active",
            Reason = nameof(SubscriptionChangeReason.BillingModeChanged),
            NewMode = preference.Mode.ToString(),
            Amount = FormatAmount(active?.AgreedAmountCents),
            CardBrand = null,
            CardLast4 = null,
            Cadence = active?.AgreedCadence,
        };

    private static TenantSubscriptionStatusChangedIntegrationEvent BuildMandateCreatedStatusEvent(
        TenantBillingPreference preference,
        TenantPaymentMandate active,
        Guid tenantId,
        string correlationId
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            TenantSubscriptionId = Guid.Empty,
            Status = "Active",
            PreviousStatus = "Active",
            Reason = nameof(SubscriptionChangeReason.MandateCreated),
            Amount = FormatAmount(active.AgreedAmountCents),
            Cadence = active.AgreedCadence,
        };

    private static string? FormatAmount(long? cents) =>
        cents is { } c ? (c / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : null;
}
