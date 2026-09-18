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
                command.Method
            ),
            ct
        );
        if (checkout.IsFailure)
            return Result.Failure<StartSeatCheckoutResponse>(checkout.Error);

        intent.AttachCheckout(checkout.Value.PaymentId, checkout.Value.CheckoutUrl, DateTime.UtcNow);
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
