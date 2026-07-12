using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;

public static class PurchaseSeatsHandler
{
    private static readonly SubscriptionStatus[] PurchasableStatuses =
        [SubscriptionStatus.Trialing, SubscriptionStatus.Active, SubscriptionStatus.GracePeriod];

    public static async Task<Result<IReadOnlyList<Guid>>> Handle(
        PurchaseSeatsCommand command,
        ISubscriptionRepository subscriptions,
        ISubscriptionSeatRepository seats,
        ISubscriptionTenantSettingsRepository settingsRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    )
    {
        var validation = await ValidateRequestAsync(command, subscriptions, seats, settingsRepository, ct);
        if (validation.IsFailure)
            return Result.Failure<IReadOnlyList<Guid>>(validation.Error);

        var (subscription, seatType) = validation.Value;
        var newSeats = BuildSeats(command, subscription, seatType);

        foreach (var seat in newSeats)
            await seats.AddAsync(seat, ct);

        var effectiveSeatCount = await CountNonTerminalSeatsAsync(seats, command.TenantId, ct) + newSeats.Count;

        await bus.PublishAsync(new SeatsPurchasedIntegrationEvent
        {
            TenantId = command.TenantId,
            PurchasingTenantId = command.TenantId,
            NewMaxUsers = effectiveSeatCount,
            SeatIds = newSeats.ConvertAll(seat => seat.Id).ToArray(),
            CorrelationId = correlation.CorrelationId,
        });
        await unitOfWork.SaveChangesAsync(ct);

        await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(command.TenantId), ct);

        logger.LogInformation(
            "Tenant {TenantId} purchased {Quantity} {SeatType} seat(s) (requested by {UserId}).",
            command.TenantId, command.Quantity, command.SeatType, command.RequestedByUserId
        );

        return Result.Success<IReadOnlyList<Guid>>(newSeats.ConvertAll(seat => seat.Id));
    }

    private static async Task<Result<(TenantSubscription Subscription, SeatType SeatType)>> ValidateRequestAsync(
        PurchaseSeatsCommand command,
        ISubscriptionRepository subscriptions,
        ISubscriptionSeatRepository seats,
        ISubscriptionTenantSettingsRepository settingsRepository,
        CancellationToken ct)
    {
        if (command.Quantity is < 1 or > 500)
        {
            return Result.Failure<(TenantSubscription, SeatType)>(
                new Error("Seat.InvalidQuantity", "Quantity must be between 1 and 500."));
        }

        if (!Enum.TryParse<SeatType>(command.SeatType, ignoreCase: true, out var seatType))
            return Result.Failure<(TenantSubscription, SeatType)>(new Error("Seat.InvalidType", "Unknown seat type."));

        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure<(TenantSubscription, SeatType)>(new Error("Subscription.NotFound", "Subscription does not exist."));

        if (Array.IndexOf(PurchasableStatuses, subscription.Status) < 0)
        {
            return Result.Failure<(TenantSubscription, SeatType)>(
                new Error("Subscription.CannotPurchaseSeats", $"Cannot purchase seats while subscription is {subscription.Status}."));
        }

        var settings = await settingsRepository.GetByTenantIdAsync(command.TenantId, ct);
        if (settings?.MaxSeatsAllowed is { } maxSeats)
        {
            var currentSeatCount = await CountNonTerminalSeatsAsync(seats, command.TenantId, ct);
            if (currentSeatCount + command.Quantity > maxSeats)
            {
                return Result.Failure<(TenantSubscription, SeatType)>(
                    new Error("Seat.MaxSeatsExceeded", $"Purchasing {command.Quantity} seat(s) would exceed the tenant's limit of {maxSeats}."));
            }
        }

        return Result.Success((subscription, seatType));
    }

    private static List<SubscriptionSeat> BuildSeats(PurchaseSeatsCommand command, TenantSubscription subscription, SeatType seatType)
    {
        var nowUtc = DateTime.UtcNow;
        var newSeats = new List<SubscriptionSeat>(command.Quantity);

        for (var i = 0; i < command.Quantity; i++)
        {
            // Precio real pendiente de integración con Billing (fuera del bounded context
            // de Subscription); se persiste en 0 hasta que exista un catálogo de precios
            // por seat/add-on (Fase 3+).
            var seat = SubscriptionSeat
                .Purchase(
                    command.TenantId,
                    seatType,
                    SeatSourceType.Plan,
                    subscription.PlanId,
                    Money.Zero("USD"),
                    subscription.BillingCycle,
                    command.AutoRenew,
                    command.RequestedByUserId,
                    nowUtc)
                .Value;
            newSeats.Add(seat);
        }

        return newSeats;
    }

    private static async Task<int> CountNonTerminalSeatsAsync(ISubscriptionSeatRepository seats, Guid tenantId, CancellationToken ct)
    {
        var tenantSeats = await seats.GetByTenantIdAsync(tenantId, ct);

        var count = 0;
        foreach (var seat in tenantSeats)
        {
            if (seat.Status is not (SeatStatus.Cancelled or SeatStatus.Expired or SeatStatus.Released))
                count++;
        }

        return count;
    }
}
