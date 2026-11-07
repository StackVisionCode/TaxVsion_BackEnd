using BuildingBlocks.Common;
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
        var request = new BillingPreferenceRequest(
            command.ProviderCode,
            command.ActorUserId,
            command.ConsentText,
            command.ConsentIpAddress,
            command.ConsentUserAgent
        );
        var parsed = BillingPreferencePolicy.ParseRequest(command.Mode, request);
        if (parsed.IsFailure)
            return Result.Failure<BillingPreferenceResponse>(parsed.Error);
        var mode = parsed.Value;

        // Precio/currency/cadence vienen del plan vigente — el request no los puede alterar.
        var terms = MandateTerms.None;
        if (mode == BillingMode.Automatic)
        {
            var resolved = await ResolveCurrentPricingAsync(subscriptions, plans, command.TenantId, ct);
            if (resolved.IsFailure)
                return Result.Failure<BillingPreferenceResponse>(resolved.Error);
            terms = resolved.Value;
        }

        var nowUtc = DateTime.UtcNow;
        var existing = await repository.GetByTenantIdAsync(command.TenantId, ct);
        var hadActiveMandateBefore = existing?.ActiveMandate() is not null;
        var previousMode = existing?.Mode;

        var applied = BillingPreferencePolicy.Apply(existing, command.TenantId, mode, request, terms, nowUtc);
        if (applied.IsFailure)
            return Result.Failure<BillingPreferenceResponse>(applied.Error);
        var preference = applied.Value;
        if (existing is null)
            await repository.AddAsync(preference, ct);

        await unitOfWork.SaveChangesAsync(ct);

        var notices = BillingPreferenceNotices.For(
            preference,
            previousMode,
            hadActiveMandateBefore,
            command.TenantId,
            correlation.CorrelationId,
            nowUtc
        );
        foreach (var notice in notices)
            await bus.PublishAsync(notice);

        var active = preference.ActiveMandate();
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

    private static async Task<Result<MandateTerms>> ResolveCurrentPricingAsync(
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(tenantId, ct);
        if (subscription is null)
            return Result.Failure<MandateTerms>(
                new Error("BillingPreference.NoSubscription", "Tenant has no subscription to anchor the mandate to.")
            );

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Result.Failure<MandateTerms>(
                new Error("BillingPreference.PlanNotFound", "Plan does not exist.")
            );

        var version = plan.GetPublishedVersion();
        if (version is null)
            return Result.Failure<MandateTerms>(
                new Error("BillingPreference.NoPublishedVersion", "Plan has no published version.")
            );

        var price = PlanPricing.ResolveBaseSubscriptionPrice(version, subscription.BillingCycle);
        if (price is null || price.Value.AmountCents <= 0)
            return Result.Failure<MandateTerms>(
                new Error("BillingPreference.NoPositivePrice", "Plan has no positive price for the current cycle.")
            );

        var cadence = subscription.BillingCycle == BillingCycle.Yearly ? "yearly" : "monthly";
        return Result.Success(new MandateTerms(price.Value.AmountCents, price.Value.Currency, cadence));
    }
}
