using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Seats.Commands.StartSeatCheckout;

public static class StartSeatCheckoutHandler
{
    private static readonly SubscriptionStatus[] PurchasableStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active,
        SubscriptionStatus.GracePeriod,
    ];

    public static async Task<Result<StartSeatCheckoutResponse>> Handle(
        StartSeatCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        ISeatPricingRepository seatPricing,
        ISeatPurchaseIntentRepository intents,
        ISeatCheckoutPaymentClient checkoutClient,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var reused = await ReuseOpenCheckoutAsync(command, intents, ct);
        if (reused is not null)
            return reused;

        var prepared = await PrepareIntentAsync(command, subscriptions, seatPricing, ct);
        if (prepared.IsFailure)
            return Result.Failure<StartSeatCheckoutResponse>(prepared.Error);

        var intent = prepared.Value;
        await intents.AddAsync(intent, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var checkout = await checkoutClient.CreateCheckoutAsync(
            new SeatCheckoutClientRequest(
                command.TenantId,
                intent.Id,
                intent.ProratedTotalCents,
                intent.Currency,
                command.PayerEmail,
                command.SuccessUrl,
                command.CancelUrl,
                IdempotencyKeyFactory.SeatCheckout(intent.Id),
                command.Provider,
                command.Method,
                intent.Quantity,
                ProratedUnit.Of(intent.ProratedTotalCents, intent.Quantity)
            ),
            ct
        );
        if (checkout.IsFailure)
            return Result.Failure<StartSeatCheckoutResponse>(checkout.Error);

        intent.AttachCheckout(
            checkout.Value.PaymentId,
            checkout.Value.CheckoutUrl,
            checkout.Value.ExpiresAtUtc,
            DateTime.UtcNow
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new StartSeatCheckoutResponse(
                intent.Id,
                checkout.Value.CheckoutUrl,
                checkout.Value.PaymentId,
                checkout.Value.ExpiresAtUtc
            )
        );
    }

    /// <summary>
    /// Guard del doble cobro: mientras la sesión de checkout anterior siga viva, la misma compra devuelve esa
    /// misma URL en vez de abrir un segundo cobro (doble clic, reintento tras un corte, volver atrás). Si lo que
    /// se pide es distinto, se rechaza: dos sesiones pagables a la vez es justo lo que hay que evitar.
    /// </summary>
    private static async Task<Result<StartSeatCheckoutResponse>?> ReuseOpenCheckoutAsync(
        StartSeatCheckoutCommand command,
        ISeatPurchaseIntentRepository intents,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var open = await intents.GetOpenByTenantAsync(command.TenantId, nowUtc, ct);
        if (open is null || !open.IsOpen(nowUtc))
            return null;

        var sameRequest =
            Enum.TryParse<SeatType>(command.SeatType, ignoreCase: true, out var seatType)
            && open.Matches(seatType, command.Quantity, open.BillingCycle, command.AutoRenew);
        if (!sameRequest)
            return Result.Failure<StartSeatCheckoutResponse>(
                new Error(
                    "Seat.CheckoutInProgress",
                    "There is already a seat purchase waiting for payment. Finish it or wait for it to expire."
                )
            );

        return Result.Success(
            new StartSeatCheckoutResponse(
                open.Id,
                open.CheckoutUrl!,
                open.SaaSPaymentId!.Value,
                open.CheckoutExpiresAtUtc!.Value
            )
        );
    }

    private static async Task<Result<SeatPurchaseIntent>> PrepareIntentAsync(
        StartSeatCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        ISeatPricingRepository seatPricing,
        CancellationToken ct
    )
    {
        if (command.Quantity is < 1 or > 500)
            return Result.Failure<SeatPurchaseIntent>(
                new Error("Seat.InvalidQuantity", "Quantity must be between 1 and 500.")
            );

        if (!Enum.TryParse<SeatType>(command.SeatType, ignoreCase: true, out var seatType))
            return Result.Failure<SeatPurchaseIntent>(new Error("Seat.InvalidType", "Unknown seat type."));

        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure<SeatPurchaseIntent>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        if (Array.IndexOf(PurchasableStatuses, subscription.Status) < 0)
            return Result.Failure<SeatPurchaseIntent>(
                new Error(
                    "Subscription.CannotPurchaseSeats",
                    $"Cannot purchase seats while subscription is {subscription.Status}."
                )
            );

        var pricing = await seatPricing.GetAsync(ct);
        if (pricing is null)
            return Result.Failure<SeatPurchaseIntent>(
                new Error("SeatPricing.NotFound", "Seat pricing catalog does not exist.")
            );

        var unitPrice = pricing.ResolveUnitPrice(seatType, subscription.BillingCycle);
        if (unitPrice.IsFailure)
            return Result.Failure<SeatPurchaseIntent>(unitPrice.Error);

        var prorated = ProrationCalculator.InitialPeriod(
            unitPrice.Value,
            subscription.CurrentPeriodStartUtc,
            subscription.CurrentPeriodEndUtc,
            DateTime.UtcNow
        );
        if (prorated.IsFailure)
            return Result.Failure<SeatPurchaseIntent>(prorated.Error);

        var proratedUnitCents = (long)Math.Round(prorated.Value.Amount * 100m, MidpointRounding.AwayFromZero);
        var totalCents = proratedUnitCents * command.Quantity;
        if (totalCents <= 0)
            return Result.Failure<SeatPurchaseIntent>(
                new Error(
                    "Seats.Checkout.NothingToCharge",
                    "This seat type is free; use the direct purchase instead of checkout."
                )
            );

        return SeatPurchaseIntent.Create(
            command.TenantId,
            seatType,
            command.Quantity,
            command.AutoRenew,
            unitPrice.Value,
            subscription.BillingCycle,
            totalCents,
            command.RequestedByUserId,
            DateTime.UtcNow
        );
    }
}
