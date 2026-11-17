using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Subscriptions.Renewals;
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
    StartRenewalCheckoutResponse? OpenCheckout = null,
    long? DiscountCents = null,
    string? DiscountSource = null,
    string? DiscountLabel = null,
    IReadOnlyList<SaaSChargeLine>? Lines = null,
    // Checkout de otro proveedor que se canceló para abrir este; el inicio cierra su sesión.
    Guid? ReplacedPaymentId = null,
    Guid? ReplacedIntentId = null
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
        ISubscriptionSeatRepository seats,
        ITenantAddOnRepository addOns,
        IAddOnDefinitionRepository definitions,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var replaced = await ReplaceOtherProviderAsync(reservation, intents, unitOfWork, ct);
        var reserved = await ReserveAsync(
            reservation,
            subscriptions,
            plans,
            intents,
            seats,
            addOns,
            definitions,
            unitOfWork,
            ct
        );
        return replaced is null || reserved.IsFailure
            ? reserved
            : Result.Success(
                reserved.Value with
                {
                    ReplacedPaymentId = replaced.PaymentId,
                    ReplacedIntentId = replaced.IntentId,
                }
            );
    }

    // Un checkout abierto con otro proveedor se cancela (y se guarda) antes de reservar el nuevo.
    private static async Task<ReplacedCheckout?> ReplaceOtherProviderAsync(
        ReserveRenewalCheckoutCommand reservation,
        IRenewalCheckoutIntentRepository intents,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var open = await intents.GetOpenByTenantAsync(reservation.TenantId, nowUtc, ct);
        if (open is null || !open.IsOpen(nowUtc) || open.OpenedWith(reservation.Request.Provider))
            return null;

        open.Cancel(reservation.Request.RequestedByUserId, nowUtc, CheckoutProviderSwitch.Reason);
        await unitOfWork.SaveChangesAsync(ct);
        return open.SaaSPaymentId is { } paymentId ? new ReplacedCheckout(open.Id, paymentId) : null;
    }

    private static async Task<Result<RenewalCheckoutReservation>> ReserveAsync(
        ReserveRenewalCheckoutCommand reservation,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IRenewalCheckoutIntentRepository intents,
        ISubscriptionSeatRepository seats,
        ITenantAddOnRepository addOns,
        IAddOnDefinitionRepository definitions,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var command = reservation.Request;
        if (command.PaysDue)
            return await ReserveDueAsync(
                command,
                subscriptions,
                plans,
                intents,
                seats,
                addOns,
                definitions,
                unitOfWork,
                ct
            );

        var reused = await ReuseOpenCheckoutAsync(command, intents, ct);
        if (reused is not null)
            return reused;

        var prepared = await PrepareIntentAsync(command, subscriptions, plans, ct);
        if (prepared.IsFailure)
            return Result.Failure<RenewalCheckoutReservation>(prepared.Error);

        var unattached = await intents.GetUnattachedByTenantAsync(command.TenantId, ct);
        if (unattached is not null)
        {
            // Guarda la edición agendada que pudo aplicarse al preparar el cobro.
            await unitOfWork.SaveChangesAsync(ct);
            return Reserved(unattached, prepared.Value.Charge);
        }

        var (intent, charge) = prepared.Value;
        await intents.AddAsync(intent, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(
            new RenewalCheckoutReservation(
                intent.Id,
                intent.Amount.Cents,
                intent.Amount.Currency,
                DiscountCents: charge.DiscountCents,
                DiscountSource: charge.Source,
                DiscountLabel: charge.Label
            )
        );
    }

    // El intent guarda el neto; el descuento se toma del cobro recién calculado si ese neto coincide.
    private static Result<RenewalCheckoutReservation> Reserved(
        SubscriptionRenewalIntent intent,
        DiscountedCharge charge
    )
    {
        var same = charge.NetCents == intent.Amount.Cents;
        return Result.Success(
            new RenewalCheckoutReservation(
                intent.Id,
                intent.Amount.Cents,
                intent.Amount.Currency,
                DiscountCents: same ? charge.DiscountCents : null,
                DiscountSource: same ? charge.Source : null,
                DiscountLabel: same ? charge.Label : null
            )
        );
    }

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

    private static async Task<Result<(SubscriptionRenewalIntent Intent, DiscountedCharge Charge)>> PrepareIntentAsync(
        StartRenewalCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure<(SubscriptionRenewalIntent, DiscountedCharge)>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        if (Array.IndexOf(RenewableStatuses, subscription.Status) < 0)
            return Result.Failure<(SubscriptionRenewalIntent, DiscountedCharge)>(
                new Error(
                    "Subscription.CannotSelfServiceRenew",
                    $"Cannot start a renewal checkout while subscription is {subscription.Status}."
                )
            );

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Result.Failure<(SubscriptionRenewalIntent, DiscountedCharge)>(
                new Error("Plan.NotFound", "Plan does not exist.")
            );

        // D11: si ya venció la edición agendada, se renueva con ella.
        subscription.ApplyScheduledVersionChangeIfDue(DateTime.UtcNow);

        // D5: se renueva al precio firmado, igual que la renovación automática.
        var planVersion = PlanPricing.FindVersion(plan, subscription.PlanVersionId);
        if (planVersion is null)
            return Result.Failure<(SubscriptionRenewalIntent, DiscountedCharge)>(
                new Error("Plan.ContractedVersionNotFound", "The contracted plan edition does not exist.")
            );

        var price = PlanPricing.ResolveBaseSubscriptionPrice(planVersion, subscription.BillingCycle);
        if (price is null || price.Value.AmountCents <= 0)
            return Result.Failure<(SubscriptionRenewalIntent, DiscountedCharge)>(
                new Error(
                    "Subscription.RenewalCheckout.NothingToCharge",
                    "This plan has no positive price for the current cycle."
                )
            );

        // El período nuevo empieza al pagar: el descuento vigente se mira con la fecha de hoy.
        var charge = DiscountedCharge.For(
            subscription,
            DiscountChargeKind.Base,
            price.Value.AmountCents,
            price.Value.Currency,
            DateTime.UtcNow
        );
        var intent = SubscriptionRenewalIntent.Create(
            command.TenantId,
            Money.FromCents(charge.NetCents, price.Value.Currency),
            subscription.BillingCycle,
            command.RequestedByUserId,
            DateTime.UtcNow
        );
        return intent.IsFailure
            ? Result.Failure<(SubscriptionRenewalIntent, DiscountedCharge)>(intent.Error)
            : Result.Success((intent.Value, charge));
    }

    // "Pay what's due": un checkout con el plan vencido (si va) y los asientos y add-ons elegidos.
    private static async Task<Result<RenewalCheckoutReservation>> ReserveDueAsync(
        StartRenewalCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IRenewalCheckoutIntentRepository intents,
        ISubscriptionSeatRepository seats,
        ITenantAddOnRepository addOns,
        IAddOnDefinitionRepository definitions,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var quote = await DueQuoteReader.ReadAsync(
            command.TenantId,
            new DueSelection(command.SeatIds ?? [], command.AddOnIds ?? []),
            subscriptions,
            plans,
            seats,
            addOns,
            definitions,
            nowUtc,
            ct
        );
        if (quote.IsFailure)
            return Result.Failure<RenewalCheckoutReservation>(quote.Error);

        var (subscription, due) = quote.Value;
        var candidate = DuePayment.Intent(
            command.TenantId,
            due,
            subscription.BillingCycle,
            command.RequestedByUserId,
            nowUtc
        );
        if (candidate.IsFailure)
            return Result.Failure<RenewalCheckoutReservation>(candidate.Error);

        var items = candidate.Value.Items.ToList();
        var open = await intents.GetOpenByTenantAsync(command.TenantId, nowUtc, ct);
        if (open is not null && open.IsOpen(nowUtc))
        {
            if (!open.Covers(due.IncludesPlan, items))
                return Result.Failure<RenewalCheckoutReservation>(
                    new Error(
                        "Subscription.Due.CheckoutOpen",
                        "A payment is already open. Finish or cancel it to change what you pay."
                    )
                );

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

        var intent = candidate.Value;
        var unattached = await intents.GetUnattachedByTenantAsync(command.TenantId, ct);
        if (
            unattached is not null
            && unattached.Covers(due.IncludesPlan, items)
            && unattached.Amount.Cents == due.TotalNetCents
        )
            intent = unattached;
        else
        {
            // Otra selección: el intento sin checkout de antes ya no sirve.
            unattached?.Cancel(command.RequestedByUserId, nowUtc, "Replaced by a new selection.");
            await intents.AddAsync(intent, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(
            new RenewalCheckoutReservation(
                intent.Id,
                intent.Amount.Cents,
                intent.Amount.Currency,
                DiscountCents: due.DiscountCents > 0 ? due.DiscountCents : null,
                DiscountSource: due.DiscountSource,
                DiscountLabel: due.DiscountLabel,
                Lines: due.Lines
            )
        );
    }
}
