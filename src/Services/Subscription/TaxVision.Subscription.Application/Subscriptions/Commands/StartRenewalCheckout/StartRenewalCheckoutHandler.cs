using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.StartRenewalCheckout;

/// <summary>
/// Crea la intención de renovación self-service y pide a PaymentApp la sesión de checkout hosteada. Solo para
/// suscripciones en lapso (PastDue/GracePeriod/Suspended/Expired) — el camino feliz (Active) renueva off-session
/// por el job. El monto es el precio del ciclo vigente, resuelto server-side. Molde: <c>StartSeatCheckoutHandler</c>.
/// </summary>
public static class StartRenewalCheckoutHandler
{
    private static readonly SubscriptionStatus[] RenewableStatuses =
    [
        SubscriptionStatus.PastDue,
        SubscriptionStatus.GracePeriod,
        SubscriptionStatus.Suspended,
        SubscriptionStatus.Expired,
    ];

    public static async Task<Result<StartRenewalCheckoutResponse>> Handle(
        StartRenewalCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IRenewalCheckoutIntentRepository intents,
        IRenewalCheckoutPaymentClient checkoutClient,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var reused = await ReuseOpenCheckoutAsync(command, intents, ct);
        if (reused is not null)
            return reused;

        var prepared = await PrepareIntentAsync(command, subscriptions, plans, ct);
        if (prepared.IsFailure)
            return Result.Failure<StartRenewalCheckoutResponse>(prepared.Error);

        var intent = prepared.Value;
        await intents.AddAsync(intent, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var checkout = await checkoutClient.CreateCheckoutAsync(
            new RenewalCheckoutClientRequest(
                command.TenantId,
                intent.Id,
                intent.AmountCents,
                intent.Currency,
                command.PayerEmail,
                command.SuccessUrl,
                command.CancelUrl,
                IdempotencyKeyFactory.RenewalCheckout(intent.Id),
                command.Provider,
                command.Method
            ),
            ct
        );
        if (checkout.IsFailure)
            return Result.Failure<StartRenewalCheckoutResponse>(checkout.Error);

        intent.AttachCheckout(
            checkout.Value.PaymentId,
            checkout.Value.CheckoutUrl,
            checkout.Value.ExpiresAtUtc,
            DateTime.UtcNow
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new StartRenewalCheckoutResponse(
                intent.Id,
                checkout.Value.CheckoutUrl,
                checkout.Value.PaymentId,
                checkout.Value.ExpiresAtUtc
            )
        );
    }

    /// <summary>
    /// Guard del doble cobro, el mismo que ya tienen asientos, add-ons y el upgrade: mientras la sesión
    /// anterior siga viva, se devuelve esa misma URL en vez de abrir un segundo cobro. Acá la renovación es
    /// siempre "la misma compra" (el precio del ciclo vigente), así que no hace falta comparar nada.
    /// </summary>
    private static async Task<Result<StartRenewalCheckoutResponse>?> ReuseOpenCheckoutAsync(
        StartRenewalCheckoutCommand command,
        IRenewalCheckoutIntentRepository intents,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var open = await intents.GetOpenByTenantAsync(command.TenantId, nowUtc, ct);
        if (open is null || !open.IsOpen(nowUtc))
            return null;

        return Result.Success(
            new StartRenewalCheckoutResponse(
                open.Id,
                open.CheckoutUrl!,
                open.SaaSPaymentId!.Value,
                open.CheckoutExpiresAtUtc!.Value
            )
        );
    }

    private static async Task<Result<SubscriptionRenewalIntent>> PrepareIntentAsync(
        StartRenewalCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure<SubscriptionRenewalIntent>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        if (Array.IndexOf(RenewableStatuses, subscription.Status) < 0)
            return Result.Failure<SubscriptionRenewalIntent>(
                new Error(
                    "Subscription.CannotSelfServiceRenew",
                    $"Cannot start a renewal checkout while subscription is {subscription.Status}."
                )
            );

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Result.Failure<SubscriptionRenewalIntent>(new Error("Plan.NotFound", "Plan does not exist."));

        var planVersion = plan.GetPublishedVersion();
        if (planVersion is null)
            return Result.Failure<SubscriptionRenewalIntent>(
                new Error("Plan.NoPublishedVersion", "Plan has no published version.")
            );

        var price = PlanPricing.ResolveBaseSubscriptionPrice(planVersion, subscription.BillingCycle);
        if (price is null || price.Value.AmountCents <= 0)
            return Result.Failure<SubscriptionRenewalIntent>(
                new Error(
                    "Subscription.RenewalCheckout.NothingToCharge",
                    "This plan has no positive price for the current cycle."
                )
            );

        return SubscriptionRenewalIntent.Create(
            command.TenantId,
            price.Value.AmountCents,
            price.Value.Currency,
            subscription.BillingCycle,
            command.RequestedByUserId,
            DateTime.UtcNow
        );
    }
}
