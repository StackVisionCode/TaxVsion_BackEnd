using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.Discounts;

/// <summary>El owner de una oficina con suscripción canjea un código de descuento.</summary>
public sealed record RedeemSubscriptionDiscountCodeCommand(Guid TenantId, string Code, Guid ActorUserId);

/// <summary>
/// Cotiza en Growth contra la base de la suscripción, reserva, confirma en el acto (el canje cuenta al aplicarse) y
/// guarda el descuento. Si la confirmación falla, cancela la reserva y no guarda nada.
/// </summary>
public static class RedeemSubscriptionDiscountCodeHandler
{
    public static async Task<Result<SubscriptionDiscountView>> Handle(
        RedeemSubscriptionDiscountCodeCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IGrowthCodeRedemptionClient growth,
        IUnitOfWork unitOfWork,
        ISubscriptionAuditLogWriter audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var redeemable = await RedeemableAsync(command, subscriptions, plans, nowUtc, ct);
        if (redeemable.IsFailure)
            return Result.Failure<SubscriptionDiscountView>(redeemable.Error);

        var (subscription, request) = redeemable.Value;
        var granted = await GrantThroughGrowthAsync(subscription, request, growth, command.ActorUserId, nowUtc, ct);
        if (granted.IsFailure)
            return Result.Failure<SubscriptionDiscountView>(granted.Error);

        var view = SubscriptionDiscountView.From(granted.Value);
        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantSubscription",
            subscription.Id,
            "TenantSubscription.DiscountCodeRedeemed",
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: view,
            reason: null,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(view);
    }

    // La suscripción, sin descuento vigente, y lo que se cotiza: la base de su plan y ciclo.
    private static async Task<Result<(TenantSubscription, GrowthCodeQuoteRequest)>> RedeemableAsync(
        RedeemSubscriptionDiscountCodeCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var code = command.Code?.Trim() ?? string.Empty;
        if (code.Length == 0)
            return Failure(new Error("Subscription.DiscountCode.Required", "Enter a code."));

        var subscription = await subscriptions.GetWithFullHistoryAsync(command.TenantId, ct);
        if (subscription is null)
            return Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        if (subscription.ActiveDiscount(nowUtc) is not null)
            return Failure(
                new Error("Subscription.Discount.AlreadyActive", "The subscription already has an active discount.")
            );

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        var version = PlanPricing.FindVersion(plan, subscription.PlanVersionId);
        var price = version is null
            ? null
            : PlanPricing.ResolveBaseSubscriptionPrice(version, subscription.BillingCycle);
        if (price is not { AmountCents: > 0 } basePrice)
            return Failure(new Error("Plan.ContractedVersionNotFound", "The contracted plan price was not found."));

        var redemptionId = Guid.NewGuid();
        return Result.Success(
            (
                subscription,
                new GrowthCodeQuoteRequest(
                    command.TenantId,
                    code,
                    subscription.PlanId,
                    basePrice.AmountCents,
                    basePrice.Currency,
                    Snapshot(
                        command.TenantId,
                        subscription.PlanId,
                        basePrice.AmountCents,
                        basePrice.Currency,
                        redemptionId
                    ),
                    redemptionId,
                    subscription.BillingCycle.ToString()
                )
            )
        );
    }

    // Cotiza, reserva y confirma; si no confirma, cancela. El descuento se pega solo con el canje confirmado,
    // porque Wolverine guarda lo rastreado al final aunque el handler falle.
    private static async Task<Result<SubscriptionDiscount>> GrantThroughGrowthAsync(
        TenantSubscription subscription,
        GrowthCodeQuoteRequest request,
        IGrowthCodeRedemptionClient growth,
        Guid actorUserId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var quote = await growth.QuoteAsync(request, ct);
        if (quote.IsFailure)
            return Result.Failure<SubscriptionDiscount>(quote.Error);

        var terms = TermsOf(quote.Value, subscription, nowUtc);
        if (terms.IsFailure)
            return Result.Failure<SubscriptionDiscount>(terms.Error);

        var reservation = await growth.ReserveAsync(request.TenantId, quote.Value.QuoteId, request.RedemptionId, ct);
        if (reservation.IsFailure)
            return Result.Failure<SubscriptionDiscount>(reservation.Error);

        var committed = await growth.CommitAsync(
            request.TenantId,
            reservation.Value,
            request.RedemptionId,
            request.SnapshotHash,
            ct
        );
        if (committed.IsFailure)
        {
            await growth.CancelAsync(request.TenantId, reservation.Value, request.RedemptionId, "Redeem failed.", ct);
            return Result.Failure<SubscriptionDiscount>(committed.Error);
        }

        return subscription.GrantDiscount(terms.Value, replace: false, actorUserId, nowUtc);
    }

    // "Once" cubre el próximo cobro de la base; "Months" y "Forever" corren desde hoy.
    private static Result<SubscriptionDiscountTerms> TermsOf(
        GrowthCodeQuote quote,
        TenantSubscription subscription,
        DateTime nowUtc
    )
    {
        if (!Enum.TryParse<DiscountDuration>(quote.Duration, out var duration))
            return Result.Failure<SubscriptionDiscountTerms>(
                new Error("Subscription.DiscountCode.Unavailable", "We couldn't read the code's duration.")
            );

        var startsAtUtc =
            duration == DiscountDuration.Once
                ? subscription.NextRenewalAtUtc ?? subscription.CurrentPeriodEndUtc
                : nowUtc;
        return SubscriptionDiscountTerms.Create(
            DiscountSource.Code,
            quote.CodeDefinitionId,
            quote.PercentageBasisPoints,
            quote.FixedAmountCents,
            quote.Currency,
            duration,
            quote.DurationMonths,
            DiscountAppliesTo.Base,
            planCode: null,
            reason: null,
            startsAtUtc,
            quote.CodeDisplay
        );
    }

    private static string Snapshot(Guid tenantId, Guid planId, long grossCents, string currency, Guid redemptionId) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId:N}|{planId:N}|{grossCents}|{currency}|{redemptionId:N}"))
        );

    private static Result<(TenantSubscription, GrowthCodeQuoteRequest)> Failure(Error error) =>
        Result.Failure<(TenantSubscription, GrowthCodeQuoteRequest)>(error);
}
