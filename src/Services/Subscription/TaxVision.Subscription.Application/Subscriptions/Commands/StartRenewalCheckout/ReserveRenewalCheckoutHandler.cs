using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.StartRenewalCheckout;

/// <summary>Paso 1 de SUB-7: confirma la intención antes de llamar a PaymentApp.</summary>
public sealed record ReserveRenewalCheckoutCommand(StartRenewalCheckoutCommand Request)
{
    public Guid TenantId => Request.TenantId;
}

/// <summary><paramref name="OpenCheckout"/> viene si ya hay una sesión viva: no se llama a PaymentApp.</summary>
public sealed record RenewalCheckoutReservation(
    Guid IntentId,
    long AmountCents,
    string Currency,
    StartRenewalCheckoutResponse? OpenCheckout = null
);

public static class ReserveRenewalCheckoutHandler
{
    private static readonly SubscriptionStatus[] RenewableStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active,
        SubscriptionStatus.PastDue,
        SubscriptionStatus.Suspended,
        SubscriptionStatus.Expired,
    ];

    public static async Task<Result<RenewalCheckoutReservation>> Handle(
        ReserveRenewalCheckoutCommand reservation,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IRenewalCheckoutIntentRepository intents,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var command = reservation.Request;
        var reused = await ReuseOpenCheckoutAsync(command, intents, ct);
        if (reused is not null)
            return reused;

        var prepared = await PrepareIntentAsync(command, subscriptions, plans, ct);
        if (prepared.IsFailure)
            return Result.Failure<RenewalCheckoutReservation>(prepared.Error);

        var unattached = await intents.GetUnattachedByTenantAsync(command.TenantId, ct);
        if (unattached is not null)
            return Reserved(unattached);

        var intent = prepared.Value;
        await intents.AddAsync(intent, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Reserved(intent);
    }

    private static Result<RenewalCheckoutReservation> Reserved(SubscriptionRenewalIntent intent) =>
        Result.Success(new RenewalCheckoutReservation(intent.Id, intent.Amount.Cents, intent.Amount.Currency));

    /// <summary>
    /// Guard del doble cobro, el mismo que ya tienen asientos, add-ons y el upgrade: mientras la sesión
    /// anterior siga viva, se devuelve esa misma URL en vez de abrir un segundo cobro. Acá la renovación es
    /// siempre "la misma compra" (el precio del ciclo vigente), así que no hace falta comparar nada.
    /// </summary>
    private static async Task<Result<RenewalCheckoutReservation>?> ReuseOpenCheckoutAsync(
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
            new RenewalCheckoutReservation(
                open.Id,
                open.Amount.Cents,
                open.Amount.Currency,
                new StartRenewalCheckoutResponse(
                    open.Id,
                    open.CheckoutUrl!,
                    open.SaaSPaymentId!.Value,
                    open.CheckoutExpiresAtUtc!.Value
                )
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
            Money.FromCents(price.Value.AmountCents, price.Value.Currency),
            subscription.BillingCycle,
            command.RequestedByUserId,
            DateTime.UtcNow
        );
    }
}
